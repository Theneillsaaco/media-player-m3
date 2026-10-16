using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Player.Core.Contracts;

namespace Player.Providers.YoutubeMusic;

/// <summary>
/// The line protocol of sidecar/ytm/server.py: one JSON object per line each way, replies
/// matched to requests by id. Also where the script is found and the call timeouts live.
/// </summary>
internal static class SidecarProtocol
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);
    public static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(45); // yt-dlp can be slow
    public static readonly TimeSpan ImportTimeout = TimeSpan.FromSeconds(60); // keyring prompts can be slow
    public static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(5);    // a big library paginates

    /// <summary>
    /// PLAYER_SIDECAR (the script itself), then PLAYER_SIDECAR_DIR (the folder that holds ytm/server.py, which is
    /// what the Nix package sets), then the sidecar folder of the repository found by walking up from the
    /// executable and from the working directory.
    /// </summary>
    public static string FindScript()
    {
        var env = Environment.GetEnvironmentVariable("PLAYER_SIDECAR");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            return env;

        var folder = Environment.GetEnvironmentVariable("PLAYER_SIDECAR_DIR");
        if (!string.IsNullOrEmpty(folder))
        {
            var inFolder = Path.Combine(folder, "ytm", "server.py");
            if (File.Exists(inFolder))
                return inFolder;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "sidecar", "ytm", "server.py");
                if (File.Exists(candidate)) 
                    return candidate;
            }
        
        throw new FileNotFoundException("sidecar/ytm/server.py not found; set PLAYER_SIDECAR to its path or PLAYER_SIDECAR_DIR to its folder");
    }
    
    public static string Request(long id, string method, object args) =>
        JsonSerializer.Serialize(new { id, method, @params = args });
    
    public static void Dispatch(string line, ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                return;
            if (!pending.TryRemove(idEl.GetInt64(), out var tcs))
                return;
            if (root.GetProperty("ok").GetBoolean())
            {
                tcs.TrySetResult(root.GetProperty("result").Clone());
                return;
            }
            
            var error = root.GetProperty("error").GetString() ?? "error";
            var authProblem = root.TryGetProperty("code", out var code) && code.GetString() is "auth_expired" or "auth_required";
            tcs.TrySetException(authProblem ? new AuthExpiredException(error) : new InvalidOperationException(error));
        }
        catch (JsonException) { /* not ours: ignore */ }
    }
}
