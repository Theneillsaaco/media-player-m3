using Player.Core;
using Player.Core.Contracts;

namespace Player.App.Services;

/// <summary>
/// Views and likes of the tracks, from the provider, looked up in the background and kept in memory. A lookup that
/// failed is left alone for a minute. Nothing here raises events: the view model asks again from its poll.
/// </summary>
internal sealed class StatsService(IBrowseProvider? source)
{
    private const long RetryAfterSeconds = 60;
    private const int MaxEntries = 200;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, TrackStats> _stats = [];
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, long> _failedAt = [];

    /// <summary>The numbers of a track, null while they are not known.</summary>
    public TrackStats? TryGet(string id)
    {
        lock (_gate)
            return _stats.TryGetValue(id, out var stats) ? stats : null;
    }

    /// <summary>A lookup of this track is running.</summary>
    public bool Pending(string id)
    {
        lock (_gate)
            return _inflight.Contains(id);
    }

    /// <summary>Starts the lookup when it is needed. Cheap to call again and again; links and files have no numbers.</summary>
    public void Request(string id)
    {
        if (source is null || id.Length != 11 || TrackSource.IsDirect(id))
            return;

        lock (_gate)
        {
            if (_stats.ContainsKey(id) || _inflight.Contains(id))
                return;
            if (_failedAt.TryGetValue(id, out var at) && Now() - at < RetryAfterSeconds)
                return;

            _inflight.Add(id);
        }

        _ = Task.Run(() => FetchAsync(id));
    }

    private async Task FetchAsync(string id)
    {
        try
        {
            var stats = await source!.Stats(id);

            lock (_gate)
            {
                if (_stats.Count >= MaxEntries)
                    _stats.Clear();

                _stats[id] = stats;
                _inflight.Remove(id);
                _failedAt.Remove(id);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("stats", "lookup failed for " + id, ex);

            lock (_gate)
            {
                _inflight.Remove(id);
                _failedAt[id] = Now();
            }
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
