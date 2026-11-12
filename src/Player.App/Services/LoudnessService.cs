using Player.Core.Contracts;

namespace Player.App.Services;

/// <summary>
/// How loud YouTube measured the tracks (only a signed-in session gets it), looked up in the background for the
/// track that plays and the ones that come next. A failed lookup waits two minutes.
/// </summary>
internal sealed class LoudnessService(IAccountProvider? source, Func<bool> signedIn)
{
    private readonly LookupCache<string, double>? _cache = source is null 
        ? null
        : new LookupCache<string, double>(
            async id =>
            {
                var level = await source.Loudness(id);
                return level == 0.0 ? double.NaN : level; // NaN: YouTube has no number for it
            },
            "loudness", TimeSpan.FromSeconds(120), 500,
            id => id.Length == 11 && !TrackSource.IsDirect(id));

    /// <summary>Goes up when an answer lands.</summary>
    public int Version => _cache?.Version ?? 0;

    /// <summary>The measured level in dB, null while it is not known (or there is none).</summary>
    public double? TryGet(string id) =>
        _cache is not null && _cache.TryGet(id, out var level) && !double.IsNaN(level) ? level : null;

    /// <summary>Starts the lookups that are needed.</summary>
    public void Want(IEnumerable<string> ids)
    {
        if (_cache is null || !signedIn())
            return;

        foreach (var id in ids)
            _cache.Request(id);
    }
}
