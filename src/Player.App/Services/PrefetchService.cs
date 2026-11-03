using Player.Core;
using Player.Core.Contracts;
using Player.Core.Playback;

namespace Player.App.Services;

/// <summary>
/// Resolves the direct stream of the tracks that play now and next, on the thread pool, and keeps the results in a
/// small in-memory cache until shortly before they expire. Nothing here raises events: <see cref="Version"/> goes up
/// when a request ends (resolved or failed) and the playback poll looks at it.
/// </summary>
internal sealed class PrefetchService(IMusicProvider? provider)
{
    /// <summary>There is a provider to ask. Without one nothing is ever resolved (playback goes straight to mpv).</summary>
    public bool Enabled => provider is not null;

    /// <summary>Goes up every time a request ends.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>The cached stream for an id, or null when there is none or it is about to expire.</summary>
    public ResolvedStream? TryGet(string id)
    {
        lock (_gate)
            return _cache.TryGetValue(id, out var stream) && Prefetch.isFresh(Now(), stream) ? stream : null;
    }

    /// <summary>The last request for this id failed a moment ago and it is not tried again yet.</summary>
    public bool Failed(string id)
    {
        lock (_gate)
            return _failedAt.TryGetValue(id, out var at) && Now() - at < RetryAfterSeconds;
    }

    /// <summary>A request for this id is running.</summary>
    public bool Resolving(string id)
    {
        lock (_gate)
            return _inflight.Contains(id);
    }

    /// <summary>
    /// Says what is wanted, most urgent first. The first id never waits for the others; the rest share
    /// <see cref="MaxParallel"/> places. What is already cached, running or failed a moment ago is left alone, and
    /// what is no longer in the list and has not started is forgotten.
    /// </summary>
    public void Want(IReadOnlyList<string> ids)
    {
        if (provider is null)
            return;

        lock (_gate)
            _wanted = [.. ids];

        Pump();
    }

    /// <summary>Drops an id (its stream failed to play), so the next request resolves it again.</summary>
    public void Forget(string id)
    {
        lock (_gate)
        {
            _cache.Remove(id);
            _order.Remove(id);
            _failedAt.Remove(id);
        }
    }

    /// <summary>Starts what the list asks for and there is room for.</summary>
    private void Pump()
    {
        if (provider is null)
            return;

        List<string> start = [];
        var now = Now();

        lock (_gate)
        {
            var running = _inflight.Count;

            for (var rank = 0; rank < _wanted.Length; rank++)
            {
                var id = _wanted[rank];

                if (_inflight.Contains(id))
                    continue;
                if (_cache.TryGetValue(id, out var stream) && Prefetch.isFresh(now, stream))
                    continue;
                if (_failedAt.TryGetValue(id, out var at) && now - at < RetryAfterSeconds)
                    continue;
                if (rank > 0 && running >= MaxParallel)
                    break; // the song that plays now would have started; the rest wait their turn

                _inflight.Add(id);
                start.Add(id);
                running++;
            }
        }

        foreach (var id in start)
            _ = ResolveAsync(provider, id);
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
        finally
        {
            Interlocked.Increment(ref _version);
            Pump(); // a place is free: the next one in line can start
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    #region Fields

    private const int MaxEntries = 8;
    private const int MaxParallel = 2; // besides the song that plays now
    private const long RetryAfterSeconds = 20; // after a failed resolve, leave that id alone for a while

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ResolvedStream> _cache = [];
    private readonly List<string> _order = []; // oldest first
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, long> _failedAt = [];
    private string[] _wanted = [];
    private int _version;

    #endregion
}

