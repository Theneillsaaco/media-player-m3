using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.Services;

/// <summary>
/// What plays after the queue ends: the radio YouTube Music builds from the last track, asked for in the background
/// while that track still plays. <see cref="Version"/> goes up when a request finishes (found or failed). A seed
/// that failed is left alone for a minute.
/// </summary>
internal sealed class AutoplayService(IBrowseProvider? source)
{
    private readonly LookupCache<string, Track[]>? _cache = source is null
        ? null
        : new LookupCache<string, Track[]>(
            source.Radio, "autoplay", TimeSpan.FromSeconds(60), 8,
            id => id.Length == 11 && !TrackSource.IsDirect(id));

    /// <summary>Goes up when a request finishes.</summary>
    public int Version => _cache?.Version ?? 0;

    /// <summary>A request is running.</summary>
    public bool Busy => _cache?.Busy ?? false;

    /// <summary>The recommendations for a seed, null while there are none.</summary>
    public Track[]? TryGet(string seedId) => _cache is not null && _cache.TryGet(seedId, out var tracks) ? tracks : null;

    public void Request(string seedId) => _cache?.Request(seedId);
}
