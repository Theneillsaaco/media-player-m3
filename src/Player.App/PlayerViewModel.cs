using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Player.Audio;
using Player.Core;
using Player.Providers;
using Player.Scrobble;
using Player.Storage;

namespace Player.App;

/// <summary>
/// What QML sees. Instantiated from Main.qml as <c>PlayerViewModel { id: player }</c>.
/// All state is refreshed by <see cref="Poll"/>, which QML calls from a Timer on the Qt
/// thread. Background work (searching, scrobbling) leaves its result in a field under a lock
/// and Poll picks it up, so no property change is ever raised from a foreign thread.
/// </summary>
public class PlayerViewModel : INotifyPropertyChanged
{
    private const string Loading = "Cargando…";

    private readonly IAudioEngine _engine = new MpvEngine();
    private readonly IMusicProvider? _provider;
    private readonly PlayerDatabase? _db;
    private readonly ScrobbleService? _scrobble;
    private readonly Random _rng = new();
    private readonly Lock _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private QueueState _queue = PlayQueue.empty;
    private IReadOnlyList<Track> _results = [];
    private ListenTracker? _tracker;
    private string _currentId = "";
    private double _knownDuration;
    private bool _sawActive;

    // Written by the search task, read by Poll.
    private bool _pendingReady;
    private IReadOnlyList<Track>? _pendingResults;
    private string? _pendingError;
    private int _searchTicket;

    public PlayerViewModel()
    {
        try { _provider = new YtSidecarProvider(); }
        catch (Exception e) { Status = "Búsqueda no disponible: " + e.Message; }

        try
        {
            _db = new PlayerDatabase(PlayerDatabase.DefaultPath);
            IScrobbler? client = LastFmConfig.load(AppPaths.ConfigFile("lastfm.json")) is { IsSome: true } credentials
                ? new LastFmClient(credentials.Value)
                : null;
            _scrobble = new ScrobbleService(client, _db, new SecretStore());
            _scrobble.Start();
            SyncScrobbler();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[player] storage unavailable: " + e.Message);
            ScrobbleStatus = "No se pudo abrir la base de datos: " + e.Message;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _scrobble?.Dispose();
            _provider?.Dispose();
            _engine.Dispose();
            _db?.Dispose();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackListModel Results { get; } = new();
    public TrackListModel Queue { get; } = new();

    public double Position { get; private set => Set(ref field, value); }
    public double Duration { get; private set => Set(ref field, value); }
    public bool Paused { get; private set => Set(ref field, value); } = true;
    public bool IsLoaded { get; private set => Set(ref field, value); }
    public bool Searching { get; private set => Set(ref field, value); }
    public string Status { get; private set => Set(ref field, value); } = "Busca una canción o pega un enlace o ruta";
    public string CurrentTitle { get; private set => Set(ref field, value); } = "";
    public string CurrentArtist { get; private set => Set(ref field, value); } = "";
    public string CurrentArt { get; private set => Set(ref field, value); } = "";
    public string RepeatMode { get; private set => Set(ref field, value); } = "off";
    public bool Shuffle { get; private set => Set(ref field, value); }
    public bool CanNext { get; private set => Set(ref field, value); }
    public bool CanPrevious { get; private set => Set(ref field, value); }
    /// <summary>Position of the current track in the queue panel (play order), -1 if none.</summary>
    public int QueuePosition { get; private set => Set(ref field, value); } = -1;
    public int QueueCount { get; private set => Set(ref field, value); }

    // Last.fm, for the settings dialog. State is one of:
    // unavailable, signedOut, authorizing, signedIn, reconnect.
    public string ScrobbleState { get; private set => Set(ref field, value); } = "unavailable";
    public string ScrobbleUser { get; private set => Set(ref field, value); } = "";
    public string ScrobbleStatus { get; private set => Set(ref field, value); } = "";
    public string ScrobbleAuthUrl { get; private set => Set(ref field, value); } = "";
    public bool ScrobbleEnabled { get; private set => Set(ref field, value); }
    public bool ScrobbleBusy { get; private set => Set(ref field, value); }
    public int ScrobblePending { get; private set => Set(ref field, value); }

    // ---- search ----

    /// <summary>A link or absolute path plays directly; anything else is searched on YouTube Music.</summary>
    public void Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;

        if (IsDirect(query))
        {
            var direct = new Track(query, query, "", [], "", 0.0, "", TrackKind.Song);
            _queue = PlayQueue.replace(_rng.Next(), [direct], 0, _queue);
            RefreshQueueModel();
            PlayCurrent();
            Poll();
            return;
        }

        if (_provider is null) return;
        var ticket = Interlocked.Increment(ref _searchTicket);
        Searching = true;
        Status = "Buscando…";
        var provider = _provider;
        _ = Task.Run(async () =>
        {
            IReadOnlyList<Track>? found = null;
            string? error = null;
            try { found = await provider.Search(query); }
            catch (Exception e) { error = e.Message; }
            lock (_gate)
            {
                if (ticket != Volatile.Read(ref _searchTicket)) return; // a newer search owns the result
                _pendingResults = found;
                _pendingError = error;
                _pendingReady = true;
            }
        });
    }

    /// <summary>Plays a search result: the queue becomes the whole result list, starting there.</summary>
    public void PlayResult(int index)
    {
        if (index < 0 || index >= _results.Count) return;
        _queue = PlayQueue.replace(_rng.Next(), _results, index, _queue);
        RefreshQueueModel();
        PlayCurrent();
        Poll();
    }

    public void AddResult(int index)
    {
        if (index < 0 || index >= _results.Count) return;
        var wasEmpty = PlayQueue.count(_queue) == 0;
        _queue = PlayQueue.add([_results[index]], _queue);
        RefreshQueueModel();
        if (wasEmpty)
        {
            _queue = PlayQueue.jumpTo(0, _queue);
            PlayCurrent();
        }
        Poll();
    }

    // ---- transport ----

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
    }

    public void Next()
    {
        if (!PlayQueue.hasNext(_queue)) return;
        _queue = PlayQueue.next(_queue);
        PlayCurrent();
        Poll();
    }

    public void Previous()
    {
        if (PlayQueue.count(_queue) == 0) return;
        if (IsLoaded && Position > 3) { _engine.Seek(0); Poll(); return; } // restart first, like every player
        _queue = PlayQueue.previous(_queue);
        PlayCurrent();
        Poll();
    }

    public void CycleRepeat()
    {
        _queue = PlayQueue.cycleRepeat(_queue);
        SyncQueueState();
    }

    public void ToggleShuffle()
    {
        _queue = PlayQueue.setShuffle(_rng.Next(), !_queue.Shuffle, _queue);
        RefreshQueueModel();
    }

    public void Seek(double seconds)
    {
        if (!IsLoaded) return;
        _engine.Seek(seconds);
        Poll();
    }

    public void SetVolume(int percent) => _engine.SetVolume(percent);

    // ---- scrobbling ----

    public void ConnectScrobbler() { _scrobble?.BeginSignIn(); SyncScrobbler(); }
    public void FinishScrobblerSignIn() { _scrobble?.CompleteSignIn(); SyncScrobbler(); }
    public void CancelScrobblerSignIn() { _scrobble?.CancelSignIn(); SyncScrobbler(); }
    public void DisconnectScrobbler() { _scrobble?.Disconnect(); SyncScrobbler(); }
    public void SetScrobbleEnabled(bool on) { _scrobble?.SetEnabled(on); SyncScrobbler(); }
    public void RetryScrobbles() { _scrobble?.RetryNow(); SyncScrobbler(); }

    private void SyncScrobbler()
    {
        if (_scrobble is null) return;
        var s = _scrobble.Snapshot;
        ScrobbleState = s.Stage switch
        {
            ScrobbleStage.SignedOut => "signedOut",
            ScrobbleStage.Authorizing => "authorizing",
            ScrobbleStage.SignedIn => "signedIn",
            ScrobbleStage.Reconnect => "reconnect",
            _ => "unavailable",
        };
        ScrobbleUser = s.User;
        ScrobbleStatus = s.Status;
        ScrobbleAuthUrl = s.AuthUrl;
        ScrobbleEnabled = s.Enabled;
        ScrobbleBusy = s.Busy;
        ScrobblePending = s.Pending;
    }

    /// <summary>Feeds the listen tracker; when the track counts it goes to the history and the queue to Last.fm.</summary>
    private void ObserveListening()
    {
        if (_tracker is not { } tracker) return;
        var (next, due) = Listening.observe(_clock.Elapsed.TotalSeconds, Position, Paused, Duration, tracker);
        _tracker = next;
        if (!due.IsSome) return;

        try { _db?.AddHistory(due.Value, next.Listened, _currentId); }
        catch (Exception e) { Console.Error.WriteLine("[player] history: " + e.Message); }
        _scrobble?.Submit(due.Value);
    }

    // ---- polling ----

    public void Poll()
    {
        TakeSearchResults();
        SyncScrobbler();

        var error = _engine.DrainEvents();
        if (error.Length > 0)
        {
            IsLoaded = false;
            _sawActive = false;
            Status = "Error: " + error;
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
            if (d > 0 && Status == Loading) Status = "Reproduciendo";
            ObserveListening();
        }
        else if (_sawActive)
        {
            OnTrackEnded();
        }
    }

    private void TakeSearchResults()
    {
        IReadOnlyList<Track>? found;
        string? error;
        lock (_gate)
        {
            if (!_pendingReady) return;
            found = _pendingResults;
            error = _pendingError;
            _pendingReady = false;
            _pendingResults = null;
            _pendingError = null;
        }

        Searching = false;
        if (error != null || found is null)
        {
            Status = "Error: " + (error ?? "sin respuesta");
            return;
        }
        _results = found;
        Results.Replace(found.Select(ToRow));
        Status = found.Count == 0 ? "Sin resultados" : found.Count + " resultados";
    }

    private void OnTrackEnded()
    {
        var next = PlayQueue.advance(_queue);
        if (next != null)
        {
            _queue = next.Value;
            PlayCurrent();
            return;
        }
        _sawActive = false;
        IsLoaded = false;
        Position = 0;
        Status = "Terminó";
        SyncQueueState();
    }

    // ---- queue plumbing ----

    /// <summary>Loads the queue's current track into mpv. Never calls Poll (it may run inside Poll).</summary>
    private void PlayCurrent()
    {
        var cur = PlayQueue.current(_queue);
        SyncQueueState();
        if (cur == null) return;

        var t = cur.Value;
        CurrentTitle = t.Title;
        CurrentArtist = t.Artist;
        CurrentArt = t.ArtUrl;
        _currentId = t.Id;
        _knownDuration = t.DurationSeconds;
        _sawActive = false;
        IsLoaded = true;
        Position = 0;
        Duration = t.DurationSeconds;
        Status = Loading;
        _engine.Load(SourceOf(t));

        _tracker = Listening.start(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), t);
        if (_tracker.Listen is { IsSome: true } listen) _scrobble?.NowPlaying(listen.Value);
    }

    /// <summary>Rebuilds the queue panel rows (the queue content or its order changed).</summary>
    private void RefreshQueueModel()
    {
        Queue.Replace(PlayQueue.ordered(_queue).Select(ToRow));
        SyncQueueState();
    }

    /// <summary>Cheap state that changes with every track change; does not touch the list rows.</summary>
    private void SyncQueueState()
    {
        QueueCount = PlayQueue.count(_queue);
        QueuePosition = PlayQueue.currentIndex(_queue) < 0 ? -1 : _queue.Position;
        CanNext = PlayQueue.hasNext(_queue);
        CanPrevious = QueueCount > 0;
        RepeatMode = PlayQueue.repeatName(_queue);
        Shuffle = _queue.Shuffle;
    }

    private static bool IsDirect(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith('/');

    private static string SourceOf(Track t) =>
        IsDirect(t.Id) ? t.Id : "https://music.youtube.com/watch?v=" + t.Id;

    private static TrackRow ToRow(Track t) => new()
    {
        VideoId = t.Id,
        Title = t.Title,
        Artist = t.Artist,
        Album = t.Album,
        Duration = Clock(t.DurationSeconds),
        ArtUrl = t.ArtUrl,
    };

    private static string Clock(double seconds)
    {
        if (seconds <= 0) return "";
        var s = (int)seconds;
        return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
