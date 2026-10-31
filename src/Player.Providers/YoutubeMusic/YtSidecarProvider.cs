using System.Text.Json;
using System.Text.RegularExpressions;
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
public partial class YtSidecarProvider : IMusicProvider, IAccountProvider, ILyricsSource, IVideoSource
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
            result.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number ? duration.GetDouble() : 0.0);
    }

    string ILyricsSource.Name => "YouTube Music";

    /// <summary>Lyrics from YouTube Music, timed when it has them. Only real YouTube ids qualify (not links or paths).</summary>
    public async Task<LyricsResult> Find(Track track)
    {
        if (!VideoId.IsMatch(track.Id))
            return new LyricsResult(false, false, false, "", "YouTube Music");

        var result = await CallAsync("lyrics", new { id = track.Id });
        var text = Text(result, "text");
        var found = result.TryGetProperty("found", out var f) && f.ValueKind == JsonValueKind.True && text.Trim().Length > 0;
        var synced = found && result.TryGetProperty("synced", out var s) && s.ValueKind == JsonValueKind.True;

        return new LyricsResult(found, false, synced, found ? text : "", "YouTube Music");
    }

    /// <summary>What YouTube Music says the track is (official video, studio audio, upload) and its counterpart.</summary>
    public async Task<VideoInfo> GetVideoInfo(string id)
    {
        var result = await CallAsync("video_info", new { id });
        return new VideoInfo(id, Text(result, "type"), Text(result, "counterpartId"), Text(result, "counterpartType"));
    }

    /// <summary>A video-only stream (up to 720p) of a video id.</summary>
    public async Task<VideoStream> GetVideoStream(string id)
    {
        var result = await CallAsync("video_stream", new { id }, SidecarProtocol.ResolveTimeout);
        var url = Text(result, "url");
        if (url.Length == 0)
            throw new InvalidOperationException("The sidecar returned no video URL");

        return new VideoStream(
            url,
            result.TryGetProperty("expires_at", out var expires) && expires.ValueKind == JsonValueKind.Number ? expires.GetInt64() : 0L,
            result.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number ? width.GetInt32() : 0,
            result.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number ? height.GetInt32() : 0);
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

    public async Task ReportPlayback(string id) =>
        await CallAsync("report_playback", new { id }, signedIn: true);

    /// <summary>How loud YouTube measured a track, in dB; 0 when it does not say (it only tells a signed-in session).</summary>
    public async Task<double> Loudness(string id)
    {
        var result = await CallAsync("loudness", new { id }, signedIn: true);
        return result.TryGetProperty("db", out var db) && db.ValueKind == JsonValueKind.Number ? db.GetDouble() : 0.0;
    }

    public async Task<string> CreatePlaylist(string title, string videoId)
    {
        var result = await CallAsync("create_playlist", new { title, videoId }, signedIn: true);
        return Text(result, "id");
    }

    public async Task DeletePlaylist(string id) =>
        await CallAsync("delete_playlist", new { id }, signedIn: true);

    /// <summary>Throws with the message "duplicate" when the playlist already holds the song.</summary>
    public async Task AddToPlaylist(string playlistId, string videoId) =>
        await CallAsync("playlist_add", new { playlistId, videoId }, signedIn: true);

    public async Task Follow(string channelId, bool following) =>
        await CallAsync("follow", new { channelId, on = following }, signedIn: true);

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

    private static readonly Regex VideoId = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    private readonly SidecarProcess _process;
    private readonly Lock _gate = new();
    private string? _cookie;

    public YtSidecarProvider(string? script = null, string? python = null) =>
        _process = new SidecarProcess(
            python ?? Environment.GetEnvironmentVariable("PLAYER_PYTHON") ?? "python3",
            script ?? SidecarProtocol.FindScript());

    #endregion
}
