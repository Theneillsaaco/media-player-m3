using System.Text;
using System.Text.Json;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Core.Lyrics;

namespace Player.Providers.Lyrics;

/// <summary>Timed lyrics from Kugou (no key). The first candidate whose duration is within three seconds is used.</summary>
public sealed class KugouClient : ILyricsSource, IDisposable
{
    public string Name => "Kugou";  

    public async Task<LyricsResult> Find(Track track)
    {
        var artist = (track.Artists is { Length: > 0 } list ? list[0] : track.Artist ?? "").Trim();
        var title = LyricTitle.shorten(track.Title ?? "");
        if (title.Length == 0)
            return Missing;

        var keyword = Uri.EscapeDataString((artist + " - " + title).Trim(' ', '-'));
        var ms = (int)Math.Round(track.DurationSeconds * 1000);

        using var search = await GetAsync($"https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword={keyword}&duration={ms}");
        if (!search.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return Missing;

        foreach (var c in candidates.EnumerateArray())
        {
            if (track.DurationSeconds > 0 && c.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                && Math.Abs(d.GetDouble() / 1000 - track.DurationSeconds) > DurationTolerance)
                continue;

            var id = Str(c, "id");
            var key = Str(c, "accesskey");
            if (id.Length == 0 || key.Length == 0)
                continue;

            using var download = await GetAsync($"https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={key}&fmt=lrc&charset=utf8");
            var content = Str(download.RootElement, "content");
            if (content.Length == 0)
                continue;

            var text = Encoding.UTF8.GetString(Convert.FromBase64String(content)).TrimStart('\uFEFF');
            return Lrc.isSynced(text) ? new LyricsResult(true, false, true, text, Name) : Missing;
        }

        return Missing;
    }

    public void Dispose() => _http.Dispose();

    private LyricsResult Missing => new(false, false, false, "", Name);

    private async Task<JsonDocument> GetAsync(string url)
    {
        using var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string Str(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? ""
        : v.ValueKind == JsonValueKind.String ? v.GetString() ?? ""
        : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "";

    #region Fields

    private const double DurationTolerance = 3.0;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    #endregion
}