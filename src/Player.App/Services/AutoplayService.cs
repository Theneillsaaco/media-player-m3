using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.Services;

/// <summary>
/// What plays after the queue ends: the radio YouTube Music builds from the last track, asked for in the background
/// while that track still plays and kept in memory. Nothing here raises events: <see cref="Version"/> goes up every
/// time a request finishes (found something or failed) and the playback poll looks at it. A seed that failed is left
/// alone for a minute.
/// </summary>
internal sealed class AutoplayService(IBrowseProvider? source)
{
    private const long RetryAfterSeconds = 60;
    private const int MaxEntries = 8;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Track[]> _found = [];
    private readonly List<string> _order = []; // oldest first
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, long> _failedAt = [];
    private int _version;

    /// <summary>Goes up when a request finishes.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>A request is running.</summary>
    public bool Busy
    {
        get { lock (_gate) return _inflight.Count > 0; }
    }

    /// <summary>The recommendations for a seed, null while there are none.</summary>
    public Track[]? TryGet(string seedId)
    {
        lock (_gate)
            return _found.TryGetValue(seedId, out var tracks) ? tracks : null;
    }

    /// <summary>Starts the request when it is needed. Cheap to call again and again; links and files have no radio.</summary>
    public void Request(string seedId)
    {
        if (source is null || seedId.Length != 11 || TrackSource.IsDirect(seedId))
            return;

        lock (_gate)
        {
            if (_found.ContainsKey(seedId) || _inflight.Contains(seedId))
                return;
            if (_failedAt.TryGetValue(seedId, out var at) && Now() - at < RetryAfterSeconds)
                return;

            _inflight.Add(seedId);
        }

        _ = Task.Run(() => FetchAsync(seedId));
    }

    private async Task FetchAsync(string seedId)
    {
        try
        {
            var tracks = await source!.Radio(seedId);

            lock (_gate)
            {
                _found[seedId] = tracks;
                _order.Remove(seedId);
                _order.Add(seedId);

                while (_order.Count > MaxEntries)
                {
                    _found.Remove(_order[0]);
                    _order.RemoveAt(0);
                }

                _inflight.Remove(seedId);
                _failedAt.Remove(seedId);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("autoplay", "radio failed for " + seedId, ex);

            lock (_gate)
            {
                _inflight.Remove(seedId);
                _failedAt[seedId] = Now();
            }
        }
        finally
        {
            Interlocked.Increment(ref _version);
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
