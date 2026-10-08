using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Player.Core;

namespace Player.Providers;

/// <summary>
/// Talks to sidecar/ytm/server.py: one persistent Python process, one JSON object per line,
/// replies matched to requests by id. If the process dies or stalls, the next call starts a
/// fresh one. Every method is safe to call from any thread.
/// </summary>
public sealed class YtSidecarProvider : IMusicProvider
{
    public static string FindScript()
    {
        var env = Environment.GetEnvironmentVariable("PLAYER_SIDECAR");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "sidecar", "ytm", "server.py");
                if (File.Exists(candidate)) return candidate;
            }
        throw new FileNotFoundException("sidecar/ytm/server.py not found; set PLAYER_SIDECAR to its path");
    }

    public async Task<IReadOnlyList<Track>> Search(string query)
    {
        var result = await CallAsync("search", new { query, limit = 30 });
        var tracks = new List<Track>();
        foreach (var item in result.EnumerateArray())
            tracks.Add(ToTrack(item));
        
        return tracks;
    }

    public async Task<Track> GetTrack(string id) => ToTrack(await CallAsync("song", new { id }));

    private Session EnsureStarted()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is { Proc.HasExited: false } alive) return _session;

            var psi = new ProcessStartInfo(_python)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(_script);

            var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + _python);
            var session = new Session(proc);
            _session = session;
            _ = Task.Run(() => ReadReplies(session));
            _ = Task.Run(() => DrainStderr(session));
            
            return session;
        }
    }
    
    private async Task<JsonElement> CallAsync(string method, object args)
    {
        for (var attempt = 0;; attempt++)
        {
            var session = EnsureStarted();
            var id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Pending[id] = tcs;

            try
            {
                var line = JsonSerializer.Serialize(new { id, method, @params = args });
                lock (session.WriteLock)
                {
                    session.Proc.StandardInput.WriteLine(line);
                    session.Proc.StandardInput.Flush();
                }

                return await tcs.Task.WaitAsync(CallTimeout);
            }
            catch (TimeoutException)
            {
                session.Pending.TryRemove(id, out _);
                Kill(session); // a stalled process is worse than a restart
                throw new TimeoutException("Youtube Music server timed out");
            }
            catch (IOException) when (attempt == 0)
            {
                // The process died under us (broken pipe or EOF). Start a new one and retry once.
                session.Pending.TryRemove(id, out _);
                Kill(session);
            }
        }
    }   

    private static async Task ReadReplies(Session session)
    {
        try
        {
            string? line;
            while ((line = await session.Proc.StandardOutput.ReadLineAsync()) is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root  = doc.RootElement;
                    
                    if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                    if (!session.Pending.TryRemove(idEl.GetInt64(), out var tcs)) continue;
                    if (root.GetProperty("ok").GetBoolean())
                        tcs.TrySetResult(root.GetProperty("result").Clone());
                    else
                        tcs.TrySetException(new InvalidOperationException(root.GetProperty("error").GetString()));
                }
                catch (JsonException) { /* not ours: ignore */ }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        
        // EOF: the process is gone. Fail whoever was waiting on it.
        foreach (var key in session.Pending.Keys)
            if (session.Pending.TryRemove(key, out var tcs)) 
                tcs.TrySetException(new IOException("Python process terminated"));
    }
    
    private static async Task DrainStderr(Session session)
    {
        try
        {
            string? line;
            while ((line = await session.Proc.StandardError.ReadLineAsync()) is not null)
                Console.Error.WriteLine("[ytm] " + line);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    
    private static void Kill(Session session)
    {
        try
        {
            session.Proc.Kill(true);
        }
        catch { /* already gone */ }
    }
    
    private static Track ToTrack(JsonElement e) => new (
        Text(e, "id"), Text(e, "title"), Text(e, "artist"), Text(e, "album"),
        e.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0.0,
        Text(e, "art"),
        Text(e, "kind") == "video" ? TrackKind.Video : TrackKind.Song);

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; 
    
    public void Dispose()
    {
        Session? session;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            session = _session;
            _session = null;
        }
        
        if (session is not null) Kill(session);
    }
    
    #region Fields

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);

    private sealed class Session(Process proc)
    {
        public Process Proc { get; } = proc;
        public ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public object WriteLock { get; } = new();
    }
    
    private readonly string _python;
    private readonly string _script;
    private readonly object _gate = new();
    private Session? _session;
    private long _nextId;
    private bool _disposed;

    
    public YtSidecarProvider(string? script = null, string? python = null)
    {
        _script = script ?? FindScript();
        _python = python ?? Environment.GetEnvironmentVariable("PLAYER_PYTHON") ?? "python3";
    }
    
    #endregion
 
}