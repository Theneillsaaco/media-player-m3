using System.Diagnostics;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Core.Playback;
using Player.Mpris;
using Player.Storage.Database;

namespace Player.App.Services;

/// <summary>
/// The playback engine room: queue, mpv, listen tracking, session save/restore and MPRIS. Plain C#,
/// Qt-free. Everything runs on the Qt thread (the view models call it from QML); the only thing a
/// foreign thread touches is the MPRIS command queue. After any change it raises <see cref="Changed"/>
/// so the view models can copy the new state.
/// While a track plays, the next one is resolved in the background and queued in mpv, so the switch
/// is instant and gapless; <see cref="_queuedNextId"/> is the id of that queued entry.
/// </summary>
internal sealed class PlaybackService
{
    #region What the UI asks for

    /// <summary>A link or absolute path: becomes the whole queue and plays.</summary>
    public void PlayDirect(string source)
    {
        var direct = new Track(source, source, "", [], "", 0.0, "", TrackKind.Song);
        _queue = PlayQueue.replace(_rng.Next(), [direct], 0, _queue);
        RefreshQueue(persist: false);
        PlayCurrent();
        Poll();
    }

    /// <summary>The queue becomes the whole list, starting at <paramref name="index"/>.</summary>
    public void PlayFrom(IReadOnlyList<Track> tracks, int index)
    {
        if (index < 0 || index >= tracks.Count)
            return;
        
        _queue = PlayQueue.replace(_rng.Next(), tracks, index, _queue);
        RefreshQueue(persist: false);
        PlayCurrent();
        Poll();
    }

    
    /// <summary>Replaces the queue with a list, in order or shuffled.</summary>
    public void PlayList(IReadOnlyList<Track> tracks, bool shuffle)
    {
        if (tracks.Count == 0) 
            return;
        
        _queue = PlayQueue.setShuffle(_rng.Next(), shuffle, _queue);
        PlayFrom(tracks, shuffle ? _rng.Next(tracks.Count) : 0);
    }
    
    public void AddFrom(IReadOnlyList<Track> tracks, int index)
    {
        if (index < 0 || index >= tracks.Count)
            return;
        
        var wasEmpty = PlayQueue.count(_queue) == 0;
        _queue = PlayQueue.add([tracks[index]], _queue);
        RefreshQueue();
        if (wasEmpty)
        {
            _queue = PlayQueue.jumpTo(0, _queue);
            PlayCurrent();
        }
        else
        {
            AfterQueueEdit(); // the new track may be what plays next
        }
        Poll();
    }
    
    public void PlayQueueItem(int position)
    {
        _queue = PlayQueue.jumpTo(position, _queue);
        PlayCurrent();
        Poll();
    }
    
    public void Toggle()
    {
        if (!IsLoaded)
        {
            // Finished or failed: play the current queue entry again.
            if (PlayQueue.currentIndex(_queue) >= 0) { PlayCurrent(); Poll(); }
            return;
        }
        
        _engine.SetPaused(!_engine.Paused);
        Poll();
        SaveProgress();
    }
    
    public void Next()
    {
        if (!PlayQueue.hasNext(_queue))
            return;
        
        _queue = PlayQueue.next(_queue);
        PlayCurrent();
        Poll();
    }
    
    public void Previous()
    {
        if (IsLoaded && Position > 3)
        {
            _engine.Seek(0); 
            _mpris?.Seeked(0); 
            Poll();
            return;
        }
        
        _queue = PlayQueue.previous(_queue);
        PlayCurrent();
        Poll();
    }
    
    public void CycleRepeat()
    {
        _queue = PlayQueue.cycleRepeat(_queue);
        RefreshQueue();
        AfterQueueEdit();
        Changed?.Invoke();
    }
    
    public void ToggleShuffle()
    {
        _queue = PlayQueue.setShuffle(_rng.Next(), !_queue.Shuffle, _queue);
        RefreshQueue();
        AfterQueueEdit();
        Changed?.Invoke();
    }
    
    public void Seek(double seconds)
    {
        if (!IsLoaded)
            return;
        
        _engine.Seek(seconds);
        _mpris?.Seeked((long)(seconds * 1e6));
        Poll();
    }
    
    public void SetVolume(int percent)
    {
        Volume = Math.Clamp(percent, 0, 100);
        _engine.SetVolume(Volume);
        Changed?.Invoke();
    }

    /// <summary>
    /// Plays the current track from another recording of it (the video version), from the same position, or
    /// back from its own with null. Used when the user asks for the video of a studio track: the picture and
    /// the sound must be the same performance. The queue, the track shown and the scrobble do not change.
    /// </summary>
    public void SwapAudio(string? videoId)
    {
        if (!IsLoaded || videoId == _audioOverride || PlayQueue.current(_queue) is not { } current)
            return;

        var wasPaused = Paused;
        _audioOverride = videoId;
        _pendingSeek = Position;
        _sawActive = false;
        _viaPrefetch = false;
        _queuedNextId = null; // loading replaces mpv's whole playlist
        _status.Text = Loading;

        _engine.Load(SourceFor(current.Value), []);
        _engine.SetVolume(Volume);
        _engine.SetPaused(wasPaused);
        AfterQueueEdit();
        Poll();
    }

    #endregion

    #region Polling

    public void Poll()
    {
        PollCore();
        PublishMpris();
        Changed?.Invoke();
    }
    
    private void PollCore()
    {
        var error = _engine.DrainEvents();
        var ended = _engine.TakeEnded();

        // mpv finished a track and went on into the entry we queued: follow it in our own state.
        if (ended && _queuedNextId is not null)
            OnEngineAdvanced();

        if (error.Length > 0)
        {
            if (!ended && _queuedNextId is { } queued && !_engine.Idle)
            {
                // The queued entry failed to open early while the current track plays on: drop it.
                _prefetch.Forget(queued);
                _queuedNextId = null;
                _engine.ClearNext();
                Log.Warn("playback", "queued stream failed to open: " + error);
            }
            else if (TryFallback())
            {
                return;
            }
            else
            {
                IsLoaded = false;
                _sawActive = false;
                _status.Text = "Error: " + error;
                return;
            }
        }
        
        Position = _engine.Position;
        var d = _engine.Duration;
        Duration = d > 0 ? d : _knownDuration;
        Paused = _engine.Paused;
        
        // idle-active can still read "yes" for a moment right after loadfile.
        if (!_engine.Idle)
        {
            _sawActive = true;
            if (d > 0 && _status.Text == Loading)
                _status.Text = "Playing";
            if (_pendingSeek > 0 && d > 0)
            {
                _engine.Seek(_pendingSeek); // a restored track resumes where it was left
                _pendingSeek = 0;
            }
            ObserveListening();
            if (!Paused && _clock.Elapsed.TotalSeconds - _lastSavedAt > 5)
                SaveProgress();
        }
        else if (_sawActive)
        {
            OnTrackEnded();
        }

        var version = _prefetch.Version;
        if (version != _seenPrefetch)
        {
            _seenPrefetch = version;
            SyncNext(); // a stream we were waiting for is ready
        }
    }
    
    private void OnTrackEnded()
    {
        var next = PlayQueue.advance(_queue);
        if (next is not null)
        {
            _queue = next.Value;
            PlayCurrent();
            return;
        }
        
        _sawActive = false;
        IsLoaded = false;
        Position = 0;
        _status.Text = "Finished";
        PersistQueue();
    }

    /// <summary>The queued entry started by itself (gapless): the queue moves on, mpv is not touched.</summary>
    private void OnEngineAdvanced()
    {
        _queuedNextId = null;
        _audioOverride = null;

        var next = PlayQueue.advance(_queue);
        if (next is null || PlayQueue.current(next.Value) is not { } current)
            return;

        _queue = next.Value;
        var t = current.Value;
        SetCurrent(t);
        _viaPrefetch = true;
        _pendingSeek = 0;
        Position = 0;
        _status.Text = "Playing";
        StartListening(t);
        RefreshQueue();
        AfterQueueEdit();
    }

    /// <summary>
    /// A direct stream from the cache failed (expired, 403): play the same track once through the
    /// yt-dlp route of mpv, which resolves it again. False when that is not what was playing.
    /// </summary>
    private bool TryFallback()
    {
        if (!_viaPrefetch || PlayQueue.current(_queue) is not { } current || TrackSource.IsDirect(current.Value.Id))
            return false;

        var t = current.Value;
        Log.Warn("playback", "direct stream failed, using the yt-dlp route");
        _prefetch.Forget(t.Id);
        _viaPrefetch = false;
        _queuedNextId = null;
        _sawActive = false;
        _pendingSeek = Position;
        _status.Text = Loading;

        _engine.Load(SourceFor(t), []);
        _engine.SetVolume(Volume);
        _engine.SetPaused(false);
        SyncNext();
        return true;
    }
    
    /// <summary>Feeds the listen tracker; when the track counts it goes to the history and the queue to Last.fm.</summary>
    private void ObserveListening()
    {
        if (_tracker is not { } tracker)
            return;
        
        var (next, due) = Listening.observe(_clock.Elapsed.TotalSeconds, Position, Paused, Duration, tracker);
        _tracker = next;
        if (!due.IsSome)
            return;

        try
        {
            _db?.AddHistory(due.Value, next.Listened, CurrentId);
        }
        catch (Exception ex)
        {
            Log.Warn("playback", " history: ", ex);
        }
        _scrobble?.Submit(due.Value);
    }

    #endregion

    #region Prefetch

    /// <summary>The queue order or settings changed: resolve what plays next and line up mpv's queued entry.</summary>
    private void AfterQueueEdit()
    {
        RequestPrefetch();
        SyncNext();
    }

    private void RequestPrefetch()
    {
        string[] ids = [.. Prefetch.upcoming(_queue).Select(t => t.Id).Where(id => !TrackSource.IsDirect(id)).Distinct()];
        _prefetch.Want(ids);
    }

    /// <summary>
    /// Makes mpv's second playlist entry the track that plays after the current one, when its stream
    /// is already resolved; otherwise leaves it empty (the normal end-of-track path then takes over).
    /// </summary>
    private void SyncNext()
    {
        if (!IsLoaded)
            return;

        var following = PlayQueue.advance(_queue);
        var track = following is null ? null : PlayQueue.current(following.Value)?.Value;

        if (track is null || TrackSource.IsDirect(track.Id))
        {
            ClearQueuedNext();
            return;
        }
        if (_queuedNextId == track.Id)
            return;

        if (_prefetch.TryGet(track.Id) is { } stream)
        {
            _engine.QueueNext(stream.Url, stream.Headers);
            _queuedNextId = track.Id;
        }
        else
        {
            ClearQueuedNext();
        }
    }

    private void ClearQueuedNext()
    {
        if (_queuedNextId is null)
            return;

        _engine.ClearNext();
        _queuedNextId = null;
    }

    #endregion
    
    #region Save and restore

    /// <summary>Where listening resumes: the playback position, or the restored one while nothing is loaded.</summary>
    private double ResumeSeconds => IsLoaded ? Position : _restoreSeek;
    
    /// <summary>Writes the whole queue. Called when it changes, not on every tick.</summary>
    private void PersistQueue()
    {
        if (_db is null)
            return;

        try
        {
            _db.SaveQueueState(_queue.Items, PlayQueue.currentIndex(_queue), ResumeSeconds, Volume,
                PlayQueue.repeatName(_queue), _queue.Shuffle);
        }
        catch (Exception ex)
        {
            Log.Warn("playback", " save queue: ", ex);
        }
    }

    /// <summary>Writes only the position and volume (every few seconds, on pause and at exit).</summary>
    public void SaveProgress()
    {
        _lastSavedAt = _clock.Elapsed.TotalSeconds;
        
        try
        {
            _db?.SaveProgress(ResumeSeconds, Volume);
        }
        catch (Exception ex)
        {
            Log.Warn("playback", "save progress", ex);
        }
    }

    /// <summary>
    /// Brings back the last session, paused, as the "Al iniciar" setting says: queue and position, the
    /// queue with the track from 0, or nothing (volume, repeat and shuffle always come back). Nothing is
    /// loaded into mpv until the user presses play, so opening the app never touches the network.
    /// </summary>
    public void Restore()
    {
        if (_db is null) 
            return;

        SavedQueue? saved;
        StartupMode mode;

        try
        {
            mode = Resume.parse(_db.GetSetting(Resume.settingKey));
            saved = _db.LoadQueueState();
        }
        catch (Exception ex)
        {
            Log.Warn("playback", "restore", ex);
            return;
        }
        
        if (saved is null)
            return;
        
        SetVolume(saved.Volume);

        var repeat = saved.Repeat switch
        {
            "one" => Player.Core.Domain.RepeatMode.One,
            "all" => Player.Core.Domain.RepeatMode.All,
            _ => Player.Core.Domain.RepeatMode.Off
        };
        
        var queue = PlayQueue.setShuffle(_rng.Next(), saved.Shuffle, PlayQueue.setRepeat(repeat, PlayQueue.empty));
        var plan = Resume.plan(mode, saved.Seconds);

        if (!plan.Queue || saved.Items.Count == 0)
        {
            _queue = queue;
            return;
        }

        _queue = PlayQueue.replace(_rng.Next(), saved.Items, Math.Clamp(saved.Index, 0, saved.Items.Count - 1), queue);
        RefreshQueue(persist: false);

        if (PlayQueue.current(_queue) is { } current)
            SetCurrent(current.Value);

        _restoreSeek = plan.Seconds;
        Position = _restoreSeek;
        _status.Text = "Restored queue: press play to continue";
    }
    
    public void Shutdown()
    {
        SaveProgress();
        _mpris?.Dispose();
    }

    #endregion
    
    #region MPRIS

    public void StartMpris()
    {
        if (!OperatingSystem.IsLinux()) 
            return;
        
        var service = new MprisService();
        _ = Task.Run(async () =>
        {
            try
            {
                await service.StartAsync();
                _mpris = service;
            }
            catch (Exception ex)
            {
                Log.Warn("playback", "mpris unavailable", ex);
                service.Dispose();
            }
        });
    }
    
    /// <summary>
    /// Runs what the desktop asked for (playerctl, media keys). QML calls it from an always-on timer,
    /// because a command can arrive while the player is idle and the main poll is not running.
    /// </summary>
    public void Pump()
    {
        if (_mpris is not { } mpris)
            return;

        var any = false;
        while (mpris.TryDequeue(out var command))
        {
            any = true;
            try
            {
                Handle(command);
            }
            catch (Exception ex)
            {
                Log.Warn("playback", "mpris command", ex);
            }
        }
        if (any)
            Poll();
        else if (!_mprisPublished)
            PublishMpris();
    }
    
    private void Handle(MprisCommand command)
    {
        switch (command.Action)
        {
            case MprisAction.Play: 
                if (!IsLoaded || Paused) 
                    Toggle(); 
                break;
            case MprisAction.Pause: 
                if (IsLoaded && !Paused) 
                    Toggle(); 
                break;
            case MprisAction.PlayPause: Toggle(); break;
            case MprisAction.Stop:
                if (IsLoaded)
                {
                    _engine.SetPaused(true);
                    _engine.Seek(0);
                    _mpris?.Seeked(0);
                }
                break;
            case MprisAction.Next: Next(); break;
            case MprisAction.Previous: Previous(); break;
            case MprisAction.SeekRelative: Seek(Math.Clamp(Position + command.Number / 1e6, 0, Duration)); break;
            case MprisAction.SeekTo: Seek(Math.Clamp(command.Number / 1e6, 0, Duration)); break;
            case MprisAction.SetVolume: SetVolume((int)Math.Round(Math.Clamp(command.Number, 0, 1) * 100)); break;
            case MprisAction.SetShuffle:
                if (_queue.Shuffle != command.Number > 0) ToggleShuffle();
                break;
            case MprisAction.SetLoop:
                _queue = PlayQueue.setRepeat(command.Text switch
                {
                    "Track" => Player.Core.Domain.RepeatMode.One,
                    "Playlist" => Player.Core.Domain.RepeatMode.All,
                    _ => Player.Core.Domain.RepeatMode.Off,
                }, _queue);
                PersistQueue();
                AfterQueueEdit();
                break;
        }
        Poll();
    }
    
    private void PublishMpris()
    {
        if (_mpris is not { } mpris) 
            return;
        
        _mprisPublished = true;

        var track = PlayQueue.current(_queue)?.Value;
        string[] artists = track is null ? []
            : track.Artists is { Length: > 0 } list ? list
            : track.Artist.Length > 0 ? [track.Artist] : [];
        
        mpris.Publish(new MprisState(
            Status: IsLoaded ? (Paused ? "Paused" : "Playing") : track is null ? "Stopped" : "Paused",
            TrackKey: track?.Id ?? "",
            Title: track?.Title ?? "",
            Artists: artists,
            Album: track?.Album ?? "",
            ArtUrl: track?.ArtUrl ?? "",
            LengthMicros: (long)(Duration * 1e6),
            PositionMicros: (long)(Position * 1e6),
            Volume: Volume / 100.0,
            Loop: RepeatMode switch { "one" => "Track", "all" => "Playlist", _ => "None" },
            Shuffle: Shuffle,
            CanGoNext: CanNext,
            CanGoPrevious: CanPrevious,
            CanPlay: QueueCount > 0,
            CanSeek: IsLoaded
        ));
    }

    #endregion

    /// <summary>Loads the queue's current track into mpv. Never calls Poll (it may run inside Poll).</summary>
    private void PlayCurrent()
    {
        var cur = PlayQueue.current(_queue);
        if (cur == null)
        {
            PersistQueue();
            return;
        }

        var t = cur.Value;
        SetCurrent(t);
        _sawActive = false;
        IsLoaded = true;
        _pendingSeek = _restoreSeek;
        _restoreSeek = 0;
        Position = _pendingSeek;
        _status.Text = Loading;
        _queuedNextId = null; // loading replaces mpv's whole playlist
        _audioOverride = null;

        if (!TrackSource.IsDirect(t.Id) && _prefetch.TryGet(t.Id) is { } stream)
        {
            _engine.Load(stream.Url, stream.Headers);
            _viaPrefetch = true;
        }
        else
        {
            _engine.Load(TrackSource.SourceOf(t), []);
            _viaPrefetch = false;
        }
        _engine.SetVolume(Volume);
        _engine.SetPaused(false);
        
        StartListening(t);
        RefreshQueue();
        AfterQueueEdit();
    }

    /// <summary>What mpv loads for a track: its own source, or the video id whose audio replaces it.</summary>
    private string SourceFor(Track t) =>
        _audioOverride is { } video ? "https://music.youtube.com/watch?v=" + video : TrackSource.SourceOf(t);

    private void SetCurrent(Track t)
    {
        CurrentTitle = t.Title;
        CurrentArtist = t.Artist;
        CurrentArt = t.ArtUrl;
        CurrentId = t.Id;
        _knownDuration = t.DurationSeconds;
        Duration = t.DurationSeconds;
    }

    private void StartListening(Track t)
    {
        _tracker = Listening.start(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), t);
        if (_tracker.Listen is { IsSome: true } listen) _scrobble?.NowPlaying(listen.Value);
    }
    
    /// <summary>The queue content or its order changed: bump the revision so the queue panel rebuilds its rows.</summary>
    private void RefreshQueue(bool persist = true)
    {
        QueueRevision++;
        
        if (persist)
            PersistQueue();
    }
    
    #region Fields

    private const string Loading = "Loading…";

    private readonly IAudioEngine _engine;
    private readonly PlayerDatabase? _db;
    private readonly ScrobbleService? _scrobble;
    private readonly PrefetchService _prefetch;
    private readonly StatusLine _status;
    private readonly Random _rng = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private QueueState _queue = PlayQueue.empty;
    private volatile MprisService? _mpris; // set by the task that connects to the session bus
    private bool _mprisPublished;
    private double _restoreSeek;  // where the restored track resumes when it is played
    private double _pendingSeek;  // the same, once the track is loading
    private double _lastSavedAt;
    private ListenTracker? _tracker;
    private double _knownDuration;
    private bool _sawActive;
    private string? _queuedNextId; // the track queued after the current one inside mpv, if any
    private bool _viaPrefetch;     // the current track plays from a resolved URL (not the yt-dlp hook)
    private string? _audioOverride; // a video id whose own audio plays instead of the track's (see SwapAudio)
    private int _seenPrefetch;

    public PlaybackService(IAudioEngine engine, PlayerDatabase? db, ScrobbleService? scrobble, PrefetchService prefetch, StatusLine status) =>
        (_engine, _db, _scrobble, _prefetch, _status) = (engine, db, scrobble, prefetch, status);
    
    public event Action? Changed;
    
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool Paused { get; private set; } = true;
    public bool IsLoaded { get; private set; }
    public int Volume { get; private set; } = 100;
    public string CurrentId { get; private set; } = "";
    public string CurrentTitle { get; private set; } = "";
    public string CurrentArtist { get; private set; } = "";
    public string CurrentArt { get; private set; } = "";
    /// <summary>The queue's current track (the playing one, or the restored one while paused), null when the queue is empty.</summary>
    public Track? CurrentTrack => PlayQueue.current(_queue)?.Value;
    /// <summary>The video id whose audio plays instead of the current track's own, null when it is the track's own.</summary>
    public string? AudioOverride => _audioOverride;
    
    /// <summary>Goes up whenever the queue's content or order changes (not when only the position moves).</summary>
    public int QueueRevision { get; private set; }
    public IEnumerable<Track> Ordered => PlayQueue.ordered(_queue);
    public int QueueCount => PlayQueue.count(_queue);
    /// <summary>Position of the current track in play order, -1 if none.</summary>
    public int QueuePosition => PlayQueue.currentIndex(_queue) < 0 ? -1 : _queue.Position;
    public bool CanNext => PlayQueue.hasNext(_queue);
    public bool CanPrevious => QueueCount > 0;
    public string RepeatMode => PlayQueue.repeatName(_queue);
    public bool Shuffle => _queue.Shuffle;

    #endregion
}
