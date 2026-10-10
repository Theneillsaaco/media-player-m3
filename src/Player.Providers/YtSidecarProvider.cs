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
public sealed class YtSidecarProvider : IMusicProvider, IAccountProvider
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
        return [.. result.EnumerateArray().Select(ToTrack)];
    }

    public async Task<Track> GetTrack(string id) => ToTrack(await CallAsync("song", new { id }));

    public async Task<AccountSession> SignIn(string cookie)
    {
        lock (_gate) _cookie = cookie; // kept on a network failure so a later call can sign in again
        try
        {
            var result = await CallAsync("auth.set", new { cookie, authuser = 0 });
            return new AccountSession(cookie, Text(result, "name"));
        }
        catch (AuthExpiredException)
        {
            lock (_gate) _cookie = null;
            throw;
        }
    }
    
    public async Task<AccountSession> ImportBrowser(string browser, string profile)
    {
        var result = await CallAsync("auth.import_browser", new { browser, profile }, ImportTimeout);
        var cookie = Text(result, "cookie");
        lock (_gate) _cookie = cookie;
        return new AccountSession(cookie, Text(result, "name"));
    }

    public async Task SignOut()
    {
        lock (_gate) _cookie = null;
        try { await CallAsync("auth.clear", new { }); }
        catch (Exception e) { Console.Error.WriteLine("[ytm] sign out: " + e.Message); }
    }

    public async Task<IReadOnlyList<PlaylistInfo>> GetLibrary()
    {
        var result = await CallAsync("library_playlists", new { }, ListTimeout, signedIn: true);
        return [.. result.EnumerateArray().Select(p => new PlaylistInfo(
            Text(p, "id"), Text(p, "title"),
            p.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0,
            Text(p, "art")))];
    }

    public async Task<IReadOnlyList<Track>> GetPlaylist(string id, int limit)
    {
        var result = await CallAsync("playlist", new { id, limit }, ListTimeout, signedIn: true);
        return [.. result.GetProperty("tracks").EnumerateArray().Select(ToTrack)];
    } 
    
    public async Task Rate(string id, bool liked) => 
        await CallAsync("rate_song", new { id, rating = liked ? "LIKE" : "INDIFFERENT" }, signedIn: true);
    
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
    
    private async Task<JsonElement> CallAsync(string method, object args, TimeSpan? timeout = null, bool signedIn = false)
    {
        for (var attempt = 0;; attempt++)
        {
            var session = EnsureStarted();
            try
            {
                if (signedIn) 
                    await EnsureSignedIn(session);
                
                return await Send(session, method, args, timeout ?? CallTimeout);
            }
            catch (IOException) when (attempt == 0)
            {
                Kill(session);
            }
        }
    }

    /// <summary>A fresh process knows nothing: hand it the saved cookie before the first signed-in call.</summary>
    private async Task EnsureSignedIn(Session session)
    {
        string? cookie;
        lock (_gate) cookie = _cookie;
        
        if (cookie is null)
            throw new AuthExpiredException("Not signed in");
        if (session.SignedIn) 
            return;

        await Send(session, "auth.set", new { cookie, authuser = 0 }, CallTimeout);
    }
    
    private async Task<JsonElement> Send(Session session, string method, object args, TimeSpan timeout)
    {
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

            var result = await tcs.Task.WaitAsync(timeout);
            if (method is "auth.set" or "auth.import_browser")
                session.SignedIn = true;
            else if (method == "auth.clear")
                session.SignedIn = false;

            return result;
        }
        catch (TimeoutException)
        {
            session.Pending.TryRemove(id, out _);
            Kill(session); // a stalled process is worse than a restart
            throw new TimeoutException("Youtube Music server timed out");
        }
        catch (IOException)
        {
            session.Pending.TryRemove(id, out _);
            throw;
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
                    {
                        tcs.TrySetResult(root.GetProperty("result").Clone());
                        continue;
                    }

                    var error = root.GetProperty("error").GetString() ?? "error";
                    tcs.TrySetException(root.TryGetProperty("code", out var code) && code.GetString() is "auth_expired" or "auth_required"
                        ? new AuthExpiredException(error)
                        : new InvalidOperationException(error));
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

    private static Track ToTrack(JsonElement e)
    {
        var artist = Text(e, "artist");
        var artists = Strings(e, "artists");

        return new Track(
            Text(e, "id"),
            Text(e, "title"),
            artist,
            artists.Length > 0 ? artists : artist.Length > 0 ? [artist] : [],
            Text(e, "album"),
            e.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0.0,
            Text(e, "art"),
            Text(e, "kind") == "video" ? TrackKind.Video : TrackKind.Song);
    }

    private static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(s => s.Length > 0)] 
            : [];
    
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
            _cookie = null;
        }
        
        if (session is not null) Kill(session);
    }
    
    #region Fields

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromSeconds(60); // keyring prompts can be slow
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(5);    // a big library paginates

    private sealed class Session(Process proc)
    {
        public Process Proc { get; } = proc;
        public ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public object WriteLock { get; } = new();
        public volatile bool SignedIn;
    }
    
    private readonly string _python;
    private readonly string _script;
    private readonly Lock _gate = new();
    private Session? _session;
    private string? _cookie;
    private long _nextId;
    private bool _disposed;
    
    public YtSidecarProvider(string? script = null, string? python = null)
    {
        _script = script ?? FindScript();
        _python = python ?? Environment.GetEnvironmentVariable("PLAYER_PYTHON") ?? "python3";
    }
    
    #endregion
 
}