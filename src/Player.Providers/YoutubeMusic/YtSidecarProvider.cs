using System.Text.Json;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;
using static Player.Providers.YoutubeMusic.TrackMapper;

namespace Player.Providers.YoutubeMusic;

/// <summary>
/// YouTube Music through sidecar/ytm/server.py (see <see cref="SidecarProcess"/> for the process and
/// <see cref="SidecarProtocol"/> for the wire format). This class holds the saved cookie and turns
/// the sidecar's replies into domain types. Every method is safe to call from any thread.
/// </summary>
public class YtSidecarProvider : IMusicProvider, IAccountProvider
{
    public static string FindScript() => SidecarProtocol.FindScript();

    public async Task<IReadOnlyList<Track>> Search(string query)
    {
        var result = await CallAsync("search", new { query, limit = 30 });
        return [.. result.EnumerateArray().Select(ToTrack)];
    }

    public async Task<Track> GetTrack(string id) => ToTrack(await CallAsync("song", new { id }));

    public async Task<ResolvedStream> Resolve(string id)
    {
        var result = await CallAsync("resolve", new { id }, SidecarProtocol.ResolveTimeout);
        var url = Text(result, "url");
        if (url.Length == 0) 
            throw new InvalidOperationException("The sidecar returned no stream URL");
        
        return new ResolvedStream(
            url,
            Strings(result, "headers"),
            result.TryGetProperty("expires_at", out var expires) && expires.ValueKind == JsonValueKind.Number ? expires.GetInt64() : 0L,
            result.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number ? duration.GetDouble() : 0.0
        );
    }

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
        var result = await CallAsync("auth.import_browser", new { browser, profile }, SidecarProtocol.ImportTimeout);
        var cookie = Text(result, "cookie");
        lock (_gate) _cookie = cookie;
        return new AccountSession(cookie, Text(result, "name"));
    }

    public async Task SignOut()
    {
        lock (_gate) _cookie = null;
        try { await CallAsync("auth.clear", new { }); }
        catch (Exception e) { Log.Warn("ytm", " sign out: ", e); }
    }

    public async Task<IReadOnlyList<PlaylistInfo>> GetLibrary()
    {
        var result = await CallAsync("library_playlists", new { }, SidecarProtocol.ListTimeout, signedIn: true);
        return [.. result.EnumerateArray().Select(p => new PlaylistInfo(
            Text(p, "id"), Text(p, "title"),
            p.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0,
            Text(p, "art")))];
    }

    public async Task<IReadOnlyList<Track>> GetPlaylist(string id, int limit)
    {
        var result = await CallAsync("playlist", new { id, limit }, SidecarProtocol.ListTimeout, signedIn: true);
        return [.. result.GetProperty("tracks").EnumerateArray().Select(ToTrack)];
    } 
    
    public async Task Rate(string id, bool liked) => 
        await CallAsync("rate_song", new { id, rating = liked ? "LIKE" : "INDIFFERENT" }, signedIn: true);

    public void Dispose()
    {
        lock (_gate) _cookie = null;
        _process.Dispose();
    }
    
    private async Task<JsonElement> CallAsync(string method, object args, TimeSpan? timeout = null, bool signedIn = false)
    {
        string? cookie = null;
        if (signedIn)
        {
            lock (_gate) cookie = _cookie;
            if (cookie is null) 
                throw new AuthExpiredException("Not signed in");
        }
        
        return await _process.CallAsync(method, args, timeout ?? SidecarProtocol.CallTimeout, cookie);
    }
    
    #region Fields

    private readonly SidecarProcess _process;
    private readonly Lock _gate = new();
    private string? _cookie;

    public YtSidecarProvider(string? script = null, string? python = null) =>
        _process = new SidecarProcess(
            python ?? Environment.GetEnvironmentVariable("PLAYER_PYTHON") ?? "python3",
            script ?? SidecarProtocol.FindScript());

    #endregion
}