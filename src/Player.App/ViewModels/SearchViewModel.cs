using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Models;
using Player.App.Services;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.ViewModels;

/// <summary>
/// Search box and results. A link or absolute path plays directly; anything else is searched on YouTube Music,
/// as songs, videos, albums, artists or playlists (<see cref="Filter"/>). The search runs on the thread pool and
/// leaves its result in a field under a lock; <see cref="Poll"/> picks it up.
/// </summary>
public class SearchViewModel : INotifyPropertyChanged
{
    private static readonly string[] Filters = ["songs", "videos", "albums", "artists", "playlists"];

    private readonly Lock _gate = new();
    private IReadOnlyList<Track> _results = [];
    private string _lastQuery = "";

    // Written by the search task, read by Poll.
    private bool _pendingReady;
    private SearchHits? _pendingHits;
    private string? _pendingError;
    private string _pendingFilter = "songs";
    private int _searchTicket;

    public SearchViewModel()
    {
        AppServices.EnsureStarted();
        AppServices.Status.Changed += Refresh;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Songs and videos.</summary>
    public TrackListModel Results { get; } = new();
    /// <summary>Albums, artists and playlists.</summary>
    public EntityListModel Entities { get; } = new();
    /// <summary>songs, videos, albums, artists or playlists.</summary>
    public string Filter { get; private set => Set(ref field, value); } = "songs";
    /// <summary>The results on show are entities, not tracks.</summary>
    public bool EntityMode { get; private set => Set(ref field, value); }
    public bool Searching { get; private set => Set(ref field, value); }
    /// <summary>Also shows playback messages ("Cargando…", "Error: …"), as the one status line always did.</summary>
    public string Status { get; private set => Set(ref field, value); } = "";

    public void Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;

        if (TrackSource.IsDirect(query))
        {
            AppServices.Playback.PlayDirect(query);
            return;
        }

        _lastQuery = query;
        RunSearch(query);
    }

    /// <summary>Changes what is searched for; with a query already made, it searches again.</summary>
    public void ChooseFilter(string filter)
    {
        if (!Filters.Contains(filter) || filter == Filter)
            return;

        Filter = filter;

        if (_lastQuery.Length > 0)
            RunSearch(_lastQuery);
    }

    /// <summary>Plays a search result: the queue becomes the whole result list, starting there.</summary>
    public void PlayResult(int index) => AppServices.Playback.PlayFrom(_results, index);

    public void AddResult(int index) => AppServices.Playback.AddFrom(_results, index);

    /// <summary>Picks up a finished search. QML calls it from a Timer while searching.</summary>
    public void Poll()
    {
        SearchHits? hits;
        string? error;
        string filter;
        lock (_gate)
        {
            if (!_pendingReady) return;
            hits = _pendingHits;
            error = _pendingError;
            filter = _pendingFilter;
            _pendingReady = false;
            _pendingHits = null;
            _pendingError = null;
        }

        Searching = false;
        if (error != null || hits is null)
        {
            AppServices.Status.Text = "Error: " + (error ?? "sin respuesta");
            return;
        }

        EntityMode = filter is "albums" or "artists" or "playlists";
        _results = hits.Tracks;
        Results.Replace(hits.Tracks.Select(Rows.FromTrack));
        Entities.Replace(hits.Entities.Select(Rows.FromEntity));

        var count = EntityMode ? hits.Entities.Length : hits.Tracks.Length;
        AppServices.Status.Text = count == 0 ? "Sin resultados" : count + " resultados";
    }

    private void RunSearch(string query)
    {
        if (AppServices.Provider is not { } provider) return;

        var filter = Filter;
        var ticket = Interlocked.Increment(ref _searchTicket);
        Searching = true;
        AppServices.Status.Text = "Buscando…";

        _ = Task.Run(async () =>
        {
            SearchHits? hits = null;
            string? error = null;
            try
            {
                hits = filter == "songs"
                    ? new SearchHits([.. await provider.Search(query)], [])
                    : await provider.SearchTyped(query, filter);
            }
            catch (Exception e) { error = e.Message; }

            lock (_gate)
            {
                if (ticket != Volatile.Read(ref _searchTicket)) return; // a newer search owns the result
                _pendingHits = hits;
                _pendingError = error;
                _pendingFilter = filter;
                _pendingReady = true;
            }
        });
    }

    private void Refresh() => Status = AppServices.Status.Text;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
