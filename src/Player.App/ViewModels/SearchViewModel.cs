using System.Text.Json;
using Player.App.Models;
using Player.App.Services;
using Player.App.ViewModels.Base;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.ViewModels;

/// <summary>
/// Search box and results. A link or absolute path plays directly; anything else is searched on YouTube Music,
/// as songs, videos, albums, artists or playlists (<see cref="Filter"/>). The search runs on the thread pool and
/// leaves its result in a field under a lock; <see cref="Poll"/> picks it up. The suggestions under the box work
/// the same way and are kept per text, so typing back over a text does not ask again. The last searches are kept in
/// the settings table and offered when the box is empty.
/// </summary>
public class SearchViewModel : ViewModelBase
{
    private static readonly string[] Filters = ["songs", "videos", "albums", "artists", "playlists"];

    private const string RecentKey = "search.recent";
    private const int RecentMax = 8;

    private readonly Lock _gate = new();
    private IReadOnlyList<Track> _results = [];
    private string _lastQuery = "";
    private List<string> _recent = [];

    // Written by the search task, read by Poll.
    private bool _pendingReady;
    private SearchHits? _pendingHits;
    private string? _pendingError;
    private string _pendingFilter = "songs";
    private int _searchTicket;

    // The same for the suggestions.
    private readonly Dictionary<string, string[]> _suggestCache = [];
    private bool _suggestReady;
    private string[]? _suggestPending;
    private string _suggestFor = "";
    private int _suggestTicket;

    public SearchViewModel()
    {
        AppServices.EnsureStarted();
        AppServices.Status.Changed += Refresh;
        LoadRecent();
        Refresh();
    }
    
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

    /// <summary>A JSON array of texts to offer under the search box.</summary>
    public string SuggestionsJson { get; private set => Set(ref field, value); } = "[]";
    /// <summary>A JSON array with the last searches, newest first. Offered while the box is empty.</summary>
    public string RecentJson { get; private set => Set(ref field, value); } = "[]";
    /// <summary>Waiting for suggestions, so the UI timer keeps polling.</summary>
    public bool Suggesting { get; private set => Set(ref field, value); }

    public void Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;

        if (TrackSource.IsDirect(query))
        {
            AppServices.Playback.PlayDirect(query);
            return;
        }

        Remember(query);
        _lastQuery = query;
        RunSearch(query);
    }

    /// <summary>Forgets the recent searches.</summary>
    public void ClearRecent()
    {
        _recent = [];
        SaveRecent();
    }

    /// <summary>Asks for the suggestions of what is typed. Links, paths and one letter get none.</summary>
    public void Suggest(string text)
    {
        text = text.Trim();
        var ticket = Interlocked.Increment(ref _suggestTicket);

        if (text.Length < 2 || TrackSource.IsDirect(text) || AppServices.Provider is not { } provider)
        {
            SuggestionsJson = "[]";
            Suggesting = false;
            return;
        }

        if (_suggestCache.TryGetValue(text, out var known))
        {
            SuggestionsJson = JsonSerializer.Serialize(known);
            Suggesting = false;
            return;
        }

        Suggesting = true;

        _ = Task.Run(async () =>
        {
            string[]? found = null;
            try { found = await provider.Suggestions(text); }
            catch (Exception e) { Log.Warn("search", "suggestions", e); }

            lock (_gate)
            {
                if (ticket != Volatile.Read(ref _suggestTicket)) return; // a newer text owns the answer
                _suggestPending = found ?? [];
                _suggestFor = text;
                _suggestReady = true;
            }
        });
    }

    /// <summary>Hides the suggestions and drops any answer still on its way.</summary>
    public void ClearSuggestions()
    {
        Interlocked.Increment(ref _suggestTicket);
        SuggestionsJson = "[]";
        Suggesting = false;
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

    public void PlayNextResult(int index) => AppServices.Playback.PlayNextFrom(_results, index);

    /// <summary>Picks up a finished search or suggestion. QML calls it from a Timer while one is running.</summary>
    public void Poll()
    {
        PollSuggestions();

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
            AppServices.Status.Text = Tr.F("status.error", error ?? Tr.T("search.noResponse"));
            return;
        }

        EntityMode = filter is "albums" or "artists" or "playlists";
        _results = hits.Tracks;
        Results.Replace(hits.Tracks.Select(Rows.FromTrack));
        Entities.Replace(hits.Entities.Select(Rows.FromEntity));

        var count = EntityMode ? hits.Entities.Length : hits.Tracks.Length;
        AppServices.Status.Text = count == 0 ? Tr.T("search.noResults") : Tr.F("search.results", count);
    }

    private void PollSuggestions()
    {
        string[]? found;
        string text;
        lock (_gate)
        {
            if (!_suggestReady) return;
            found = _suggestPending;
            text = _suggestFor;
            _suggestReady = false;
            _suggestPending = null;
        }

        Suggesting = false;
        if (found is null) return;

        if (_suggestCache.Count >= 60)
            _suggestCache.Clear();

        _suggestCache[text] = found;
        SuggestionsJson = JsonSerializer.Serialize(found);
    }

    private void RunSearch(string query)
    {
        if (AppServices.Provider is not { } provider) return;

        var filter = Filter;
        var ticket = Interlocked.Increment(ref _searchTicket);
        Searching = true;
        AppServices.Status.Text = Tr.T("search.searching");

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

    // ---- recent searches ----

    private void LoadRecent()
    {
        try
        {
            if (AppServices.Db?.GetSetting(RecentKey) is { Length: > 0 } json)
                _recent = [.. (JsonSerializer.Deserialize<string[]>(json) ?? []).Take(RecentMax)];
        }
        catch (Exception e)
        {
            Log.Warn("search", "read recent", e);
            _recent = [];
        }

        RecentJson = JsonSerializer.Serialize(_recent);
    }

    /// <summary>Puts a query first in the list, without a repeat and with at most <see cref="RecentMax"/> entries.</summary>
    private void Remember(string query)
    {
        _recent.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, query);

        if (_recent.Count > RecentMax)
            _recent.RemoveRange(RecentMax, _recent.Count - RecentMax);

        SaveRecent();
    }

    private void SaveRecent()
    {
        RecentJson = JsonSerializer.Serialize(_recent);

        try { AppServices.Db?.SetSetting(RecentKey, RecentJson); }
        catch (Exception e) { Log.Warn("search", "save recent", e); }
    }

    private void Refresh() => Status = AppServices.Status.Text;
}
