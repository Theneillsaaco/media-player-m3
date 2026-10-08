using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Player.Audio;
using Player.Core;
using Player.Providers;

namespace PlayerApp;

/// <summary>
/// What QML sees. Instantiated from Main.qml as <c>PlayerViewModel { id: player }</c>.
/// All state is refreshed by <see cref="Poll"/>, which QML calls from a Timer on the Qt
/// thread. Background work (searching) leaves its result in a field under a lock and
/// Poll picks it up, so no property change is ever raised from a foreign thread.
/// </summary>
public class PlayerViewModel : INotifyPropertyChanged
{
    private const string Loading = "Cargando…";

    private readonly IAudioEngine _engine = new MpvEngine();
    private readonly IMusicProvider? _provider;
    private readonly Random _rng = new();
    private readonly object _gate = new();

    private QueueState _queue = PlayQueue.empty;
    private IReadOnlyList<Track> _results = Array.Empty<Track>();
    private double _knownDuration;
    private bool _sawActive;

    // Written by the search task, read by Poll.
    private bool _pendingReady;
    private IReadOnlyList<Track>? _pendingResults;
    private string? _pendingError;
    private int _searchTicket;

    private double _position, _duration;
    private bool _paused = true, _isloaded, _searching, _shuffle, _canNext, _canPrevious;
    private int _queuePosition = -1, _queueCount;
    private string _status = "Busca una canción o pega un enlace o ruta";
    private string _currentTitle = "", _currentArtist = "", _currentArt = "", _repeatMode = "off";

    public PlayerViewModel()
    {
        try { _provider = new YtSidecarProvider(); }
        catch (Exception e) { _status = "Búsqueda no disponible: " + e.Message; }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _provider?.Dispose();
            _engine.Dispose();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackListModel Results { get; } = new();
    public TrackListModel Queue { get; } = new();

    public double Position { get => _position; private set => Set(ref _position, value); }
    public double Duration { get => _duration; private set => Set(ref _duration, value); }
    public bool Paused { get => _paused; private set => Set(ref _paused, value); }
    public bool IsLoaded { get => _isloaded; private set => Set(ref _isloaded, value); }
    public bool Searching { get => _searching; private set => Set(ref _searching, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string CurrentTitle { get => _currentTitle; private set => Set(ref _currentTitle, value); }
    public string CurrentArtist { get => _currentArtist; private set => Set(ref _currentArtist, value); }
    public string CurrentArt { get => _currentArt; private set => Set(ref _currentArt, value); }
    public string RepeatMode { get => _repeatMode; private set => Set(ref _repeatMode, value); }
    public bool Shuffle { get => _shuffle; private set => Set(ref _shuffle, value); }
    public bool CanNext { get => _canNext; private set => Set(ref _canNext, value); }
    public bool CanPrevious { get => _canPrevious; private set => Set(ref _canPrevious, value); }
    /// <summary>Position of the current track in the queue panel (play order), -1 if none.</summary>
    public int QueuePosition { get => _queuePosition; private set => Set(ref _queuePosition, value); }
    public int QueueCount { get => _queueCount; private set => Set(ref _queueCount, value); }

    // ---- search ----

    /// <summary>A link or absolute path plays directly; anything else is searched on YouTube Music.</summary>
    public void Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;

        if (IsDirect(query))
        {
            var direct = new Track(query, query, "", "", 0.0, "", TrackKind.Song);
            _queue = PlayQueue.replace(_rng.Next(), new[] { direct }, 0, _queue);
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
        _queue = PlayQueue.add(new[] { _results[index] }, _queue);
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

    // ---- polling ----

    public void Poll()
    {
        TakeSearchResults();

        var error = _engine.DrainEvents();
        if (error.Length > 0)
        {
            IsLoaded = false;
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
        _knownDuration = t.DurationSeconds;
        _sawActive = false;
        IsLoaded = true;
        Position = 0;
        Duration = t.DurationSeconds;
        Status = Loading;
        _engine.Load(SourceOf(t));
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
