using Player.Core.Contracts;

namespace Player.App.Services;

/// <summary>Views and likes of the tracks, looked up in the background and kept in memory. A failed lookup waits a minute.</summary>
internal sealed class StatsService(IBrowseProvider? source)
{
    private readonly LookupCache<string, TrackStats>? _cache = source is null 
        ? null 
        : new LookupCache<string, TrackStats>(
            source.Stats, "stats", TimeSpan.FromSeconds(60), 200,
            id => id.Length == 11 && !TrackSource.IsDirect(id));

    /// <summary>The numbers of a track, null while they are not known.</summary>
    public TrackStats? TryGet(string id) =>
        _cache is not null && _cache.TryGet(id, out var stats) ? stats : null;

    /// <summary>A lookup of this track is running.</summary>
    public bool Pending(string id) => _cache?.Pending(id) ?? false;

    /// <summary>Starts the lookup when it is needed. Cheap to call again and again; links and files have no numbers.</summary>
    public void Request(string id) => _cache?.Request(id);
}
