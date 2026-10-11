using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Player.Core;
using Player.Core.Contracts;

namespace Player.Providers.YoutubeMusic;

/// <summary>
/// The persistent Python process behind the sidecar. If it dies or stalls, the next call starts a
/// fresh one. A fresh process knows nothing, so calls that need the account pass the saved cookie
/// and it is sent with auth.set first. Safe to call from any thread.
/// </summary>
internal sealed class SidecarProcess(string python, string script) : IDisposable
{
    /// <param name="cookie">When not null, the process is signed in with it first if it is not already.</param>
    public async Task<JsonElement> CallAsync(string method, object args, TimeSpan timeout, string? cookie = null)
    {
        for (var attempt = 0;; attempt++)
        {
            var session = EnsureStarted();
            try
            {
                if (cookie is not null && !session.SignedIn) 
                    await Send(session, "auth.set", new { cookie, authuser = 0 }, SidecarProtocol.CallTimeout);
                
                return await Send(session, method, args, timeout);
            }
            catch (IOException) when (attempt == 0)
            {
                Kill(session);
            }
        }
    }

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
    
    private Session EnsureStarted()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is { Proc.HasExited: false } alive) 
                return alive;

            var psi = new ProcessStartInfo(python)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(script);
            
            var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + python);
            var session = new Session(proc);
            _session = session;
            _ = Task.Run(() => ReadReplies(session));
            _ = Task.Run(() => DrainStderr(session));

            return session;
        }
    }
    
    private async Task<JsonElement> Send(Session session, string method, object args, TimeSpan timeout)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Pending[id] = tcs;

        try
        {
            var line = SidecarProtocol.Request(id, method, args);
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
            while (await session.Proc.StandardOutput.ReadLineAsync() is { } line)
            {
                SidecarProtocol.Dispatch(line, session.Pending);
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
            while (await session.Proc.StandardError.ReadLineAsync() is { } line)
                Log.Info("ytm", line);
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
    
    #region Fields

    private sealed class Session(Process proc)
    {
        public Process Proc { get; } = proc;
        public ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public object WriteLock { get; } = new();
        public volatile bool SignedIn;
    }
    
    private readonly Lock _gate = new();
    private Session? _session;
    private long _nextId;
    private bool _disposed;

    #endregion
}