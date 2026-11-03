using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Player.Core;
using Player.Core.Playback;

namespace Player.Storage.Cache;

/// <summary>
/// Whole songs on disk. The app hands it the stream URL it already resolved and the song is downloaded in the
/// background, one at a time, in 10 MiB range requests, to a temporary file that is renamed into place only when it
/// is complete (size checked against the server's), so a half song is never played. Songs are kept up to a limit,
/// least recently used out first, but never the ones <see cref="Protect"/> names (what plays now and next). Memory
/// stays flat: a download streams to disk through one 80 KB buffer. Safe to call from any thread.
/// </summary>
public sealed class AudioCache : IDisposable
{
    /// <summary>Goes up whenever a download ends (done or failed) or the cache changes.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>How many songs are kept at most; 0 means the cache is off.</summary>
    public int Limit
    {
        get { lock (_gate) return _limit; }
    }

    /// <summary>The songs on disk and the space they take.</summary>
    public (int Files, long Bytes) Stats
    {
        get
        {
            lock (_gate)
                return (_files.Count, _files.Values.Sum(e => e.Bytes));
        }
    }
    
    /// <summary>The path of a complete song, null when it is not cached. Counts as a use.</summary>
    public string? Get(string id)
    {
        string path;
        var now = DateTime.UtcNow;

        lock (_gate)
        {
            if (_limit == 0 || !_files.TryGetValue(id, out var entry))
                return null;

            if (!File.Exists(entry.Path))
            {
                _files.Remove(id);
                return null;
            }

            _files[id] = entry with { Used = now.Ticks };
            path = entry.Path;
        }

        try { File.SetLastWriteTimeUtc(path, now); }
        catch { /* only the order of eviction suffers */ }

        return path;
    }

    public bool IsCached(string id)
    {
        lock (_gate) 
            return _limit > 0 && _files.ContainsKey(id);
    }
    
    /// <summary>A download of this song is queued or running.</summary>
    public bool IsPending(string id)
    {
        lock (_gate)
            return _known.Contains(id);
    }

    /// <summary>
    /// Says what matters now, most urgent first (the song that plays, then the next ones). Those are never evicted,
    /// the downloads wait in this order, and a queued or running download of anything else is dropped.
    /// </summary>
    public void Protect(IReadOnlyList<string> ids)
    {
        lock (_gate)
        {
            _rank.Clear();
            for (var i = 0; i < ids.Count; i++)
                _rank.TryAdd(ids[i], i);

            _queue.RemoveAll(job =>
            {
                var drop = !_rank.ContainsKey(job.Id);
                if (drop)
                    _failedAt.Remove(job.Id);
                return drop;
            });
            
            if (_activeId is { } active && !_rank.ContainsKey(active))
                _active?.Cancel();
        }
    }
    
    /// <summary>
    /// Downloads a song whose stream URL is known. Does nothing when the cache is off, the song is already here or
    /// on its way, or it failed a moment ago. Headers are "Name: value" lines, as the resolver gives them.
    /// </summary>
    public void Add(string id, string url, string[] headers)
    {
        if (!Name.IsMatch(id) || url.Length == 0)
            return;

        lock (_gate)
        {
            if (_disposed || _limit == 0 || _files.ContainsKey(id) || _known.Contains(id))
                return;
            if (_failedAt.TryGetValue(id, out var at) && DateTime.UtcNow - at < RetryAfter)
                return;

            _known.Add(id);
            _queue.Add(new Job(id, url, headers));
            
            if (!_running)
            {
                _running = true;
                _ = WorkAsync();
            }
        }
    }
    
    /// <summary>Drops one song (it would not play: the file is bad).</summary>
    public void Remove(string id)
    {
        string? path = null;

        lock (_gate)
        {
            if (_files.Remove(id, out var entry))
                path = entry.Path;
        }
        
        TryDelete(path);
        Interlocked.Increment(ref _version);
    }
    
    /// <summary>Changes the limit; 0 turns the cache off and empties it.</summary>
    public void SetLimit(int limit)
    {
        List<string> paths;

        lock (_gate)
        {
            _limit = CachePolicy.normalizeLimit(limit);

            if (_limit == 0)
            {
                _queue.Clear();
                _known.Clear();
                _active?.Cancel();
                paths = [.. _files.Values.Select(e => e.Path)];
                _files.Clear();
            }
            else 
                paths = TrimLocked();
        }
        
        foreach (var path in paths)
            TryDelete(path);
        
        Interlocked.Increment(ref _version);
    }
    
    /// <summary>Deletes every song. A song that plays right now keeps playing: the file is only unlinked.</summary>
    public void Clear()
    {
        List<string> paths;

        lock (_gate)
        {
            _queue.Clear();
            _known.Clear();
            _active?.Cancel();
            paths = [.. _files.Values.Select(e => e.Path)];
            _files.Clear();
        }
        
        foreach (var path in paths)
            TryDelete(path);
        
        Interlocked.Increment(ref _version);
    }
    
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            
            _disposed = true;
            _queue.Clear();
        }
        
        try { _life.Cancel(); }
        catch { /* already gone */ }
    }
    
    #region the worker
    
    private async Task WorkAsync()
    {
        while (true)
        {
            Job job;
            CancellationTokenSource cts;

            lock (_gate)
            {
                if (_disposed || _limit == 0 || _queue.Count == 0)
                {
                    _running = false;
                    return;
                }
                
                job = _queue.OrderBy(j => _rank.GetValueOrDefault(j.Id, int.MaxValue)).First();
                _queue.Remove(job);

                cts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
                cts.CancelAfter(JobTimeout);
                (_active, _activeId) = (cts, job.Id);
            }

            try
            {
                var done = await DownloadAsync(job, cts.Token);
                Install(job.Id, done.Temp, done.Type);
            }
            catch (OperationCanceledException)
            {
                // dropped on purpose (the user moved on) or the app is closing
            }
            catch (Exception ex)
            {
                Log.Warn("cache", "download failed for " + job.Id, ex);
                lock (_gate) _failedAt[job.Id] = DateTime.UtcNow;
            }
            finally
            {
                lock (_gate)
                {
                    _known.Remove(job.Id);
                    (_active, _activeId) = (null, null);
                }
                
                cts.Dispose();
                Interlocked.Increment(ref _version);
            }
        }
    }
    
    /// <summary>The whole song into a temporary file, by ranges. Throws unless every byte the server promised arrived.</summary>
    private async Task<(string Temp, string? Type)> DownloadAsync(Job job, CancellationToken ct)
    {
        var temp = System.IO.Path.Combine(_dir, ".part-" + job.Id + "-" + Guid.NewGuid().ToString("N")[..8]);
        var buffer = new byte[80 * 1024];

        try
        {
            string? type = null;
            long written = 0, total = -1;

            await using (var file = new FileStream(temp, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
            {
                while (total < 0 || written < total)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
                    Apply(request, job.Headers);
                    request.Headers.Range = new RangeHeaderValue(written, written + ChunkBytes - 1);

                    using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    var content = response.Content.Headers;

                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        if (content.ContentRange is not { From: { } first, Length: { } size } || first != written)
                            throw new IOException("the server answered another range");

                        total = size;
                    }
                    else if (response.StatusCode != HttpStatusCode.OK || written != 0)
                    {
                        throw new IOException("unexpected status " + (int)response.StatusCode);
                    }

                    type ??= content.ContentType?.MediaType;
                    var promised = content.ContentLength;
                    if (total > MaxFileBytes || promised > MaxFileBytes)
                        throw new IOException("too big to be a song");

                    long copied = 0;
                    await using (var body = await response.Content.ReadAsStreamAsync(ct))
                    {
                        int read;
                        while ((read = await body.ReadAsync(buffer, ct)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), ct);
                            copied += read;

                            if (written + copied > MaxFileBytes)
                                throw new IOException("too big to be a song");
                        }
                    }

                    if (copied == 0 || (promised is { } want && copied != want))
                        throw new IOException("the chunk came short");

                    written += copied;

                    if (response.StatusCode == HttpStatusCode.OK)
                        total = written; // the server ignored the range and sent everything
                }
            }

            return (temp, type);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }
    
    private static void Apply(HttpRequestMessage request, string[] headers)
    {
        var hasAgent = false;

        foreach (var line in headers)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            
            // never a cookie, and not what the request sets by itself
            if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Range", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content-", StringComparison.OrdinalIgnoreCase))
                continue;
            
            hasAgent |= name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase);
            request.Headers.TryAddWithoutValidation(name, value);
        }
        
        if (!hasAgent)
            request.Headers.TryAddWithoutValidation("User-Agent", DefaultAgent);
    }
    
    private void Install(string id, string temp, string? type)
    {
        var final = System.IO.Path.Combine(_dir, id + ExtensionFor(type));
        List<string> evicted;

        lock (_gate)
        {
            if (_disposed || _limit == 0)
            {
                TryDelete(temp);
                return;
            }

            try
            {
                File.Move(temp, final, overwrite: true);
            }
            catch (Exception ex)
            {
                TryDelete(temp);
                Log.Warn("cache", "could not keep " + id, ex);
                return;
            }
            
            _files[id] = new Entry(final, new FileInfo(final).Length, DateTime.UtcNow.Ticks);
            _failedAt.Remove(id);
            evicted = TrimLocked();
        }
        
        foreach (var path in evicted)
            TryDelete(path);
    }
    
    #endregion
    
    /// <summary>Forgets the songs the policy sends out and returns their paths; the caller deletes them outside the lock.</summary>
    private List<string> TrimLocked()
    {
        var gone = CachePolicy.evict(_limit, _rank.Keys, _files.Select(p => (p.Key, p.Value.Used)));
        List<string> paths = [];
        
        foreach (var id in gone)
            if (_files.Remove(id, out var entry))
                paths.Add(entry.Path);
        
        return paths;
    }
    
    /// <summary>Picks up what an earlier run left: complete songs are indexed, half downloads are deleted.</summary>
    private void Scan()
    {
        List<string> stale = [];

        foreach (var path in Directory.EnumerateFiles(_dir))
        {
            var file = System.IO.Path.GetFileName(path);
            var id = System.IO.Path.GetFileNameWithoutExtension(file);

            if (file.StartsWith(".part-", StringComparison.Ordinal) || !Name.IsMatch(id))
            {
                stale.Add(path);
                continue;
            }
            
            var info = new FileInfo(path);
            if (info.Length == 0)
            {
                stale.Add(path);
                continue;
            }
            
            _files[id] = new Entry(path, info.Length, info.LastWriteTimeUtc.Ticks);
        }
        
        foreach (var path in stale)
            TryDelete(path);

        List<string> paths;
        lock (_gate) paths = TrimLocked();
        
        foreach (var path in paths)
            TryDelete(path);
    }
    
    private static string ExtensionFor(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "audio/webm" or "video/webm" => ".webm",
        "audio/mp4" or "video/mp4" => ".m4a",
        "audio/mpeg" => ".mp3",
        "audio/ogg" => ".ogg",
        _ => ".bin"
    };
    
    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        
        try { File.Delete(path); }
        catch { /* already gone, or in use: the next scan cleans it */ }
    }
    
    #region Fields

    private const long ChunkBytes = 10L << 20;
    private const long MaxFileBytes = 256L << 20; // a song is a few MB; this only stops a stream that never ends
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    private static readonly Regex Name = new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
    private const string DefaultAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan }; // every job has its own timeout

    private sealed record Entry(string Path, long Bytes, long Used);

    private sealed record Job(string Id, string Url, string[] Headers);

    private readonly string _dir;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _files = [];
    private readonly List<Job> _queue = [];
    private readonly HashSet<string> _known = []; // queued or downloading
    private readonly Dictionary<string, int> _rank = []; // id -> urgency, 0 is the song that plays now
    private readonly Dictionary<string, DateTime> _failedAt = [];
    private readonly CancellationTokenSource _life = new();
    private CancellationTokenSource? _active;
    private string? _activeId;
    private bool _running;
    private bool _disposed;
    private int _limit;
    private int _version;

    public AudioCache(string directory, int limit)
    {
        _dir = directory;
        _limit = CachePolicy.normalizeLimit(limit);

        Directory.CreateDirectory(_dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Scan();
    }

    #endregion
}