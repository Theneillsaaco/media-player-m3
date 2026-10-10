using System.Collections.Concurrent;
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
    private const string LikedList = "LM"; // YouTube Music's automatic Liked Music playlist

    private readonly IAudioEngine _engine = new MpvEngine();
    private readonly IMusicProvider? _provider;
    private readonly PlayerDatabase? _db;
    private readonly ScrobbleService? _scrobble;
    private readonly AccountService? _account;
    private readonly ConcurrentQueue<Action> _ui = new(); // results of background work, applied by Poll
    private readonly Random _rng = new();
    private readonly Lock _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private QueueState _queue = PlayQueue.empty;
    private IReadOnlyList<Track> _results = [];
    private IReadOnlyList<Track> _listTracks = [];
    private IReadOnlyList<PlaylistInfo> _playlists = [];
    private HashSet<string> _likedIds = [];
    private bool _libraryLoaded;
    private int _jobs;
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
        YtSidecarProvider? yt = null;
        try { _provider = yt = new YtSidecarProvider(); }
        catch (Exception e) { Status = "Búsqueda no disponible: " + e.Message; }

        try
        {
            _db = new PlayerDatabase(PlayerDatabase.DefaultPath);
            var secrets = new SecretStore();
            _likedIds = [.. _db.LoadTracks(LikedList).Select(t => t.Id)];
            _account = new AccountService(yt, _db, secrets);
            _account.Start();
            SyncAccount();
            IScrobbler? client = LastFmConfig.load(AppPaths.ConfigFile("lastfm.json")) is { IsSome: true } credentials
                ? new LastFmClient(credentials.Value)
                : null;
            _scrobble = new ScrobbleService(client, _db, secrets);
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
    /// <summary>The user's playlists (title, "N canciones", cover).</summary>
    public TrackListModel Library { get; } = new();
    /// <summary>The tracks of the playlist that is open.</summary>
    public TrackListModel Tracks { get; } = new();

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

    // YouTube Music account. State is one of: unavailable, signedOut, signedIn, expired.
    public string AccountState { get; private set => Set(ref field, value); } = "unavailable";
    public string AccountName { get; private set => Set(ref field, value); } = "";
    public string AccountStatus { get; private set => Set(ref field, value); } = "";
    public bool AccountBusy { get; private set => Set(ref field, value); }
    /// <summary>Anything in the background that the UI timer must keep polling for.</summary>
    public bool Working { get; private set => Set(ref field, value); }

    public string LibraryStatus { get; private set => Set(ref field, value); } = "";
    /// <summary>Id of the open playlist, "" while the playlist list is showing.</summary>
    public string OpenListId { get; private set => Set(ref field, value); } = "";
    public string OpenListTitle { get; private set => Set(ref field, value); } = "";
    public string ListStatus { get; private set => Set(ref field, value); } = "";
    public int ListCount { get; private set => Set(ref field, value); }

    public bool CanLike { get; private set => Set(ref field, value); }
    public bool Liked { get; private set => Set(ref field, value); }

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
    public void PlayResult(int index) => PlayFrom(_results, index);

    public void AddResult(int index) => AddFrom(_results, index);

    private void PlayFrom(IReadOnlyList<Track> tracks, int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        _queue = PlayQueue.replace(_rng.Next(), tracks, index, _queue);
        RefreshQueueModel();
        PlayCurrent();
        Poll();
    }

    private void AddFrom(IReadOnlyList<Track> tracks, int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        var wasEmpty = PlayQueue.count(_queue) == 0;
        _queue = PlayQueue.add([tracks[index]], _queue);
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

    // ---- account and library ----

    public void ImportBrowserSession(string browser) { _account?.ImportBrowser(browser); SyncAccount(); }
    public void UseCookie(string text) { _account?.SignInWithCookie(text); SyncAccount(); }
    public void SignOutAccount() { _account?.SignOut(); SyncAccount(); }

    /// <summary>Loads the playlist list the first time the library is shown.</summary>
    public void LoadLibrary()
    {
        if (_account is null || AccountState != "signedIn" || _libraryLoaded) return;
        LibraryStatus = "Cargando biblioteca…";
        Background(Task.Run(() => _account.GetLibraryAsync()),
            lists =>
            {
                _libraryLoaded = true;
                _playlists = lists;
                Library.Replace(lists.Select(ToRow));
                LibraryStatus = lists.Count == 0 ? "No hay listas en tu biblioteca" : "";
            },
            e => LibraryStatus = ErrorText(e));
    }

    /// <summary>Opens a playlist: the cached tracks show at once, fresh ones replace them when they arrive.</summary>
    public void OpenPlaylist(int index)
    {
        if (_account is null || index < 0 || index >= _playlists.Count) return;
        var list = _playlists[index];
        var id = list.Id;
        OpenListId = id;
        OpenListTitle = list.Title;

        var cached = SafeCache(() => _db?.LoadTracks(id)) ?? [];
        SetListTracks(cached);
        ListStatus = cached.Count == 0 ? "Cargando…" : "Actualizando…";

        Background(Task.Run(async () =>
            {
                var tracks = await _account.GetPlaylistAsync(id);
                SafeCache(() => { _db?.SaveTracks(id, tracks); return 0; });
                return tracks;
            }),
            tracks =>
            {
                if (id == LikedList) ReplaceLiked(tracks);
                if (OpenListId != id) return; // the user moved on
                SetListTracks(tracks);
                ListStatus = tracks.Count == 0 ? "Esta lista está vacía" : "";
            },
            e =>
            {
                if (OpenListId != id) return;
                ListStatus = cached.Count == 0 ? ErrorText(e) : "No se pudo actualizar: " + e.Message;
            });
    }

    public void CloseList()
    {
        OpenListId = "";
        OpenListTitle = "";
        ListStatus = "";
        SetListTracks([]);
    }

    /// <summary>Replaces the queue with the open playlist, in order or shuffled.</summary>
    public void PlayList(bool shuffle)
    {
        if (_listTracks.Count == 0) return;
        _queue = PlayQueue.setShuffle(_rng.Next(), shuffle, _queue);
        PlayFrom(_listTracks, shuffle ? _rng.Next(_listTracks.Count) : 0);
    }

    public void PlayListItem(int index) => PlayFrom(_listTracks, index);

    public void AddListItem(int index) => AddFrom(_listTracks, index);

    /// <summary>Likes or unlikes the playing track on YouTube Music.</summary>
    public void ToggleLike()
    {
        if (_account is null || !CanLike) return;
        var id = _currentId;
        var like = !_likedIds.Contains(id);
        SetLiked(id, like);

        Background(Task.Run(() => _account.RateAsync(id, like)),
            _ => { },
            e =>
            {
                SetLiked(id, !like); // it did not go through: put the heart back
                Status = "No se pudo cambiar el Me gusta: " + e.Message;
            });
    }

    private void SetLiked(string id, bool liked)
    {
        if (liked) _likedIds.Add(id); else _likedIds.Remove(id);
        UpdateLikeState();
    }

    /// <summary>Fetches Liked Music in the background so the heart knows every liked song.</summary>
    private void RefreshLiked()
    {
        if (_account is null) return;
        Background(Task.Run(async () =>
            {
                var tracks = await _account.GetPlaylistAsync(LikedList);
                SafeCache(() => { _db?.SaveTracks(LikedList, tracks); return 0; });
                return tracks;
            }),
            ReplaceLiked,
            e => Console.Error.WriteLine("[player] liked songs: " + e.Message));
    }

    private void ReplaceLiked(IReadOnlyList<Track> tracks)
    {
        _likedIds = [.. tracks.Select(t => t.Id)];
        UpdateLikeState();
    }

    private void UpdateLikeState()
    {
        CanLike = AccountState == "signedIn" && IsLoaded && _currentId.Length > 0 && !IsDirect(_currentId);
        Liked = CanLike && _likedIds.Contains(_currentId);
    }

    private void SetListTracks(IReadOnlyList<Track> tracks)
    {
        _listTracks = tracks;
        Tracks.Replace(tracks.Select(ToRow));
        ListCount = tracks.Count;
    }

    private void SyncAccount()
    {
        if (_account is null) return;
        var s = _account.Snapshot;
        var state = s.Stage switch
        {
            AccountStage.SignedOut => "signedOut",
            AccountStage.SignedIn => "signedIn",
            AccountStage.Expired => "expired",
            _ => "unavailable",
        };
        var wasSignedIn = AccountState == "signedIn";
        AccountState = state;
        AccountName = s.Name;
        AccountStatus = s.Status;
        AccountBusy = s.Busy;

        if (state == "signedIn" && !wasSignedIn)
        {
            _libraryLoaded = false;
            RefreshLiked();
        }
        else if (state != "signedIn" && wasSignedIn)
        {
            _libraryLoaded = false;
            _playlists = [];
            _likedIds = [];
            Library.Replace([]);
            CloseList();
            LibraryStatus = "";
        }
        RefreshWorking();
    }

    private void RefreshWorking() => Working = _jobs > 0 || AccountBusy;

    /// <summary>Runs a task that started on a pool thread; its result is applied on the UI thread by Poll.</summary>
    private void Background<T>(Task<T> task, Action<T> done, Action<Exception> failed)
    {
        _jobs++;
        RefreshWorking();
        _ = task.ContinueWith(t => _ui.Enqueue(() =>
        {
            _jobs--;
            RefreshWorking();
            if (t.IsFaulted) failed(t.Exception!.GetBaseException());
            else if (t.IsCompletedSuccessfully) done(t.Result);
        }), TaskScheduler.Default);
    }

    private static string ErrorText(Exception e) =>
        e is AuthExpiredException ? "La sesión caducó: vuelve a importarla en Ajustes" : "Error: " + e.Message;

    private static T? SafeCache<T>(Func<T?> read)
    {
        try { return read(); }
        catch (Exception e)
        {
            Console.Error.WriteLine("[player] cache: " + e.Message);
            return default;
        }
    }

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
        PollCore();
        UpdateLikeState();
    }

    private void PollCore()
    {
        while (_ui.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Console.Error.WriteLine("[player] background result: " + e); }
        }

        TakeSearchResults();
        SyncScrobbler();
        SyncAccount();

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

    private static TrackRow ToRow(PlaylistInfo p) => new()
    {
        VideoId = p.Id,
        Title = p.Title,
        Artist = p.Count switch { 0 => "", 1 => "1 canción", var n => $"{n} canciones" },
        ArtUrl = p.ArtUrl,
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
