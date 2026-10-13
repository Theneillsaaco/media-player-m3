using System.Net;
using System.Text.Json;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.Providers.Lyrics;

/// <summary>
/// Lyrics from LRCLIB (https://lrclib.net): free, no key, with timed lyrics for many songs. It asks for the exact
/// match first (artist, title, album and duration) and falls back to a search, accepting only a result whose
/// duration is within two seconds of the track's, so lyrics of another version are not shown.
/// </summary>
public sealed class LrclibClient : ILyricsSource, IDisposable
{
    public const string UserAgent = "Player/0.3 (https://github.com/Theneillsaaco)";
    private const string Api = "https://lrclib.net/api/";
    private const double DurationTolerance = 2.0;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public LrclibClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public string Name => "LRCLIB";

    public async Task<LyricsResult> Find(Track track)
    {
        var artist = (track.Artists is { Length: > 0 } list ? list[0] : track.Artist ?? "").Trim();
        var title = (track.Title ?? "").Trim();
        var album = track.Album ?? "";
        if (artist.Length == 0 || title.Length == 0)
            return Missing;

        var duration = track.DurationSeconds;

        if (album.Length > 0 && duration > 0)
        {
            var exact = await GetAsync(
                $"get?artist_name={Escape(artist)}&track_name={Escape(title)}&album_name={Escape(album)}&duration={(int)Math.Round(duration)}");

            if (exact is { ValueKind: JsonValueKind.Object } entry && Fits(entry, duration) && ToResult(entry) is { Found: true } result)
                return result;
        }

        var found = await GetAsync($"search?artist_name={Escape(artist)}&track_name={Escape(title)}");
        if (found is not { ValueKind: JsonValueKind.Array } candidates)
            return Missing;

        JsonElement? best = null;
        var bestScore = double.MaxValue;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object || !Fits(candidate, duration) || !HasContent(candidate))
                continue;

            // Timed lyrics first, then the closest duration.
            var score = (Text(candidate, "syncedLyrics").Length > 0 ? 0.0 : 1000.0) + DurationGap(candidate, duration);
            if (score < bestScore)
                (best, bestScore) = (candidate, score);
        }

        return best is { } pick ? ToResult(pick) : Missing;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }

    private static LyricsResult Missing => new(false, false, false, "", "LRCLIB");

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool IsInstrumental(JsonElement e) =>
        e.TryGetProperty("instrumental", out var v) && v.ValueKind == JsonValueKind.True;

    private static bool HasContent(JsonElement e) =>
        IsInstrumental(e) || Text(e, "syncedLyrics").Trim().Length > 0 || Text(e, "plainLyrics").Trim().Length > 0;

    private static double DurationGap(JsonElement e, double wanted) =>
        wanted > 0 && e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
            ? Math.Abs(d.GetDouble() - wanted)
            : 0.0;

    /// <summary>A candidate fits when the durations agree (or one of them is unknown).</summary>
    private static bool Fits(JsonElement e, double wanted)
    {
        if (wanted <= 0 || !e.TryGetProperty("duration", out var d) || d.ValueKind != JsonValueKind.Number)
            return true;

        return Math.Abs(d.GetDouble() - wanted) <= DurationTolerance;
    }

    private static LyricsResult ToResult(JsonElement e)
    {
        var synced = Text(e, "syncedLyrics");
        if (synced.Trim().Length > 0)
            return new LyricsResult(true, false, true, synced, "LRCLIB");

        var plain = Text(e, "plainLyrics");
        if (plain.Trim().Length > 0)
            return new LyricsResult(true, false, false, plain, "LRCLIB");

        return IsInstrumental(e) ? new LyricsResult(true, true, false, "", "LRCLIB") : Missing;
    }

    /// <summary>The JSON of a reply, null for 404 (not found); other failures throw so they are not mistaken for "no lyrics".</summary>
    private async Task<JsonElement?> GetAsync(string path)
    {
        using var response = await _http.GetAsync(Api + path);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
