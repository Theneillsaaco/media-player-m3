using Player.Core;
using Player.Core.Contracts;

namespace Player.App.Services;

/// <summary>
/// How loud YouTube measured the tracks, looked up in the background (for the one that plays and the ones that come
/// next) and kept in memory. YouTube only gives it to a signed-in session, so without one nothing is asked. A lookup
/// that failed is left alone for two minutes. Nothing here raises events: <see cref="Version"/> goes up when an answer
/// lands and the playback poll looks at it.
/// </summary>
internal sealed class LoudnessService(IAccountProvider? source, Func<bool> signedIn)
{
    private const long RetryAfterSeconds = 120;
    private const int MaxEntries = 500;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, double> _levels = []; // NaN: YouTube has no number for it
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, long> _failedAt = [];
    private int _version;

    /// <summary>Goes up when an answer lands.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>The measured level in dB, null while it is not known (or there is none).</summary>
    public double? TryGet(string id)
    {
        lock (_gate)
            return _levels.TryGetValue(id, out var level) && !double.IsNaN(level) ? level : null;
    }

    /// <summary>Starts the lookups that are needed.</summary>
    public void Want(IEnumerable<string> ids)
    {
        if (source is null || !signedIn())
            return;

        List<string> start = [];
        var now = Now();

        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (id.Length != 11 || TrackSource.IsDirect(id) || _levels.ContainsKey(id) || _inflight.Contains(id))
                    continue;
                if (_failedAt.TryGetValue(id, out var at) && now - at < RetryAfterSeconds)
                    continue;

                _inflight.Add(id);
                start.Add(id);
            }
        }

        foreach (var id in start)
            _ = FetchAsync(id);
    }

    private async Task FetchAsync(string id)
    {
        try
        {
            var level = await Task.Run(() => source!.Loudness(id));

            lock (_gate)
            {
                if (_levels.Count >= MaxEntries)
                    _levels.Clear();

                _levels[id] = level == 0.0 ? double.NaN : level;
                _inflight.Remove(id);
                _failedAt.Remove(id);
            }

            Interlocked.Increment(ref _version);
        }
        catch (Exception ex)
        {
            Log.Warn("loudness", "lookup failed for " + id, ex);

            lock (_gate)
            {
                _inflight.Remove(id);
                _failedAt[id] = Now();
            }
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
