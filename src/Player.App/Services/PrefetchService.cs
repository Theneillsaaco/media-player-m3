using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Core.Playback;

namespace Player.App.Services;

/// <summary>
/// Resolves the direct stream of the tracks that play next, on the thread pool, and keeps the
/// results in a small in-memory cache until shortly before they expire. Nothing here raises events:
/// <see cref="Version"/> goes up when a result lands and the playback poll looks at it.
/// </summary>
internal sealed class PrefetchService(IMusicProvider? provider)
{
    /// <summary>Goes up every time a stream is added to the cache.</summary>
    public int Version => Volatile.Read(ref _version);
    
    /// <summary>The cached stream for an id, or null when there is none or it is about to expire.</summary>
    public ResolvedStream? TryGet(string id)
    {
        lock (_gate)
            return _cache.TryGetValue(id, out var stream) && Prefetch.isFresh(Now(), stream) ? stream : null;
    }
    
    /// <summary>Starts resolving the ids that are neither cached, in flight nor recently failed.</summary>
    public void Want(IReadOnlyList<string> ids)
    {
        if (provider is null)
            return;
        
        List<string> start = [];
        var now = Now();

        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (_inflight.Contains(id))
                    continue;
                if (_cache.TryGetValue(id, out var stream) && Prefetch.isFresh(now, stream))
                    continue;
                if (_failedAt.TryGetValue(id, out var at) && now - at < RetryAfterSeconds)
                    continue;
                
                _inflight.Add(id);
                start.Add(id);
            }
        }

        foreach (var id in start)
            _ = ResolveAsync(provider, id);
    }
    
    /// <summary>Drops an id (its stream failed to play), so the next request resolves it again.</summary>
    public void Forget(string id)
    {
        lock (_gate)
        {
            _cache.Remove(id);
            _order.Remove(id);
        }
    }
    
    private async Task ResolveAsync(IMusicProvider source, string id)
    {
        try
        {
            var stream = await Task.Run(() => source.Resolve(id));

            lock (_gate)
            {
                _cache[id] = stream;
                _order.Remove(id);
                _order.Add(id);

                while (_order.Count > MaxEntries)
                {
                    _cache.Remove(_order[0]);
                    _order.RemoveAt(0);
                }

                _inflight.Remove(id);
                _failedAt.Remove(id);
            }

            Interlocked.Increment(ref _version);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _inflight.Remove(id);
                _failedAt[id] = Now();
            }
            
            Log.Warn("prefetch", "resolve failed for " + id, ex);
        }
    }
    
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    #region Fields

    private const int MaxEntries = 6;
    private const long RetryAfterSeconds = 30; // after a failed resolve, leave that id alone for a while

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ResolvedStream> _cache = [];
    private readonly List<string> _order = []; // oldest first
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, long> _failedAt = [];
    private int _version;

    #endregion
}