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
        if (PlayQueue.currentIndex(_queue) == 0)
            return;
        if (IsLoaded && Position > 3)
        {
            _engine.Seek(0); 
            _mpris?.Seeked(0); 
            Poll();
            return;
        }
        
        PlayCurrent();
        Poll();
    }
    
    public void CycleRepeat()
    {
        _queue = PlayQueue.cycleRepeat(_queue);
        RefreshQueue();
        Changed?.Invoke();
    }
    
    public void ToggleShuffle()
    {
        _queue = PlayQueue.setShuffle(_rng.Next(), !_queue.Shuffle, _queue);
        RefreshQueue();
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
        if (error.Length > 0)
        {
            IsLoaded = false;
            _sawActive = false;
            _status.Text = "Error: " + error;
            return;
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
    /// Brings back the last queue, position, volume, repeat and shuffle, paused. Nothing is loaded into
    /// mpv until the user presses play, so opening the app never touches the network.
    /// </summary>
    public void Restore()
    {
        if (_db is null) 
            return;

        SavedQueue? saved;

        try
        {
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
        if (saved.Items.Count == 0)
            return;

        var repeat = saved.Repeat switch
        {
            "one" => Player.Core.Domain.RepeatMode.One,
            "all" => Player.Core.Domain.RepeatMode.All,
            _ => Player.Core.Domain.RepeatMode.Off
        };
        
        var queue = PlayQueue.setShuffle(_rng.Next(), saved.Shuffle, PlayQueue.setRepeat(repeat, PlayQueue.empty));
        _queue = PlayQueue.replace(_rng.Next(), saved.Items, Math.Clamp(saved.Index, 0, saved.Items.Count - 1), queue);
        RefreshQueue(persist: false);

        if (PlayQueue.current(_queue) is { } current)
        {
            var t = current.Value;
            CurrentTitle = t.Title;
            CurrentArtist = t.Artist;
            CurrentArt = t.ArtUrl;
            CurrentId = t.Id;
            _knownDuration = t.DurationSeconds;
            Duration = t.DurationSeconds;
        }
        _restoreSeek = Math.Max(0, saved.Seconds);
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
        CurrentTitle = t.Title;
        CurrentArtist = t.Artist;
        CurrentArt = t.ArtUrl;
        CurrentId = t.Id;
        _knownDuration = t.DurationSeconds;
        _sawActive = false;
        IsLoaded = true;
        _pendingSeek = _restoreSeek;
        _restoreSeek = 0;
        Position = _pendingSeek;
        Duration = t.DurationSeconds;
        _status.Text = Loading;
        
        _engine.Load(TrackSource.SourceOf(t));
        _engine.SetVolume(Volume);
        _engine.SetPaused(false);
        
        _tracker = Listening.start(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), t);
        if (_tracker.Listen is { IsSome: true } listen) _scrobble?.NowPlaying(listen.Value);
        RefreshQueue();
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

    public PlaybackService(IAudioEngine engine, PlayerDatabase? db, ScrobbleService? scrobble, StatusLine status) =>
        (_engine, _db, _scrobble, _status) = (engine, db, scrobble, status);
    
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