using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Models;
using Player.App.Services;
using Player.Core.Domain;

namespace Player.App.ViewModels;

/// <summary>
/// Search box and results. A link or absolute path plays directly; anything else is searched on YouTube Music.
/// The search runs on the thread pool and leaves its result in a field under a lock; <see cref="Poll"/> picks it up.
/// </summary>
public class SearchViewModel : INotifyPropertyChanged
{
    private readonly Lock _gate = new();
    private IReadOnlyList<Track> _results = [];

    // Written by the search task, read by Poll.
    private bool _pendingReady;
    private IReadOnlyList<Track>? _pendingResults;
    private string? _pendingError;
    private int _searchTicket;

    public SearchViewModel()
    {
        AppServices.EnsureStarted();
        AppServices.Status.Changed += Refresh;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackListModel Results { get; } = new();
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

        if (AppServices.Provider is not { } provider) return;
        var ticket = Interlocked.Increment(ref _searchTicket);
        Searching = true;
        AppServices.Status.Text = "Buscando…";
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
    public void PlayResult(int index) => AppServices.Playback.PlayFrom(_results, index);

    public void AddResult(int index) => AppServices.Playback.AddFrom(_results, index);

    /// <summary>Picks up a finished search. QML calls it from a Timer while searching.</summary>
    public void Poll()
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
            AppServices.Status.Text = "Error: " + (error ?? "sin respuesta");
            return;
        }
        _results = found;
        Results.Replace(found.Select(Rows.FromTrack));
        AppServices.Status.Text = found.Count == 0 ? "Sin resultados" : found.Count + " resultados";
    }

    private void Refresh() => Status = AppServices.Status.Text;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
