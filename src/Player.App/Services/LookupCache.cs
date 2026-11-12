using System.Diagnostics.CodeAnalysis;
using Player.Core;

namespace Player.App.Services;

/// <summary>
/// A keyed value that is fetched in the background, kept in memory (oldest out first) and not asked again for
/// <c>retryAfter</c> after a failure. Stats, autoplay and loudness all did this by hand. Nothing here raises events:
/// <see cref="Version"/> goes up every time a request ends, and the poll looks at it.
/// </summary>
internal sealed class LookupCache<TKey, TValue>(
    Func<TKey, Task<TValue>> fetch,
    string name,
    TimeSpan retryAfter,
    int maxEntries,
    Func<TKey, bool>? accepts = null) where TKey : notnull
{
    public int Version => Volatile.Read(ref _version);

    public bool Busy
    {
        get { lock (_gate) return _inflight.Count > 0; }
    }
    
    public bool Pending(TKey key)
    {
        lock (_gate)
            return _inflight.Contains(key);
    }
    
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
            return _values.TryGetValue(key, out value);
    }
    
    /// <summary>Starts the lookup when it is needed. Cheap to call again and again.</summary>
    public void Request(TKey key)
    {
        if (accepts is not null && !accepts(key))
            return;
        
        lock (_gate)
        {
            if (_values.ContainsKey(key) || _inflight.Contains(key))
                return;
            if (_failedAt.TryGetValue(key, out var at) && Now() - at < retryAfter.TotalSeconds)
                return;
            
            _inflight.Add(key);
        }
        
        _ = FetchAsync(key);
    }
    
    private async Task FetchAsync(TKey key)
    {
        try
        {
            var value = await Task.Run(() => fetch(key));

            lock (_gate)
            {
                _values[key] = value;
                _order.Remove(key);
                _order.Add(key);

                while (_order.Count > maxEntries)
                {
                    _values.Remove(_order[0]);
                    _order.RemoveAt(0);
                }

                _inflight.Remove(key);
                _failedAt.Remove(key);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(name, "lookup failed for " + key, ex);

            lock (_gate)
            {
                _inflight.Remove(key);
                _failedAt[key] = Now();
            }
        }
        finally
        {
            Interlocked.Increment(ref _version);
        }
    }
    
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    #region Fields

    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, TValue> _values = [];
    private readonly List<TKey> _order = []; // oldest first
    private readonly HashSet<TKey> _inflight = [];
    private readonly Dictionary<TKey, long> _failedAt = [];
    private int _version;

    #endregion
}