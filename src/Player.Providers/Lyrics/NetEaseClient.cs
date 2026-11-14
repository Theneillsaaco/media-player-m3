using System.Collections.Concurrent;
using System.Text.Json;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Core.Lyrics;
using static Player.Providers.JsonRead;

namespace Player.Providers.Lyrics;

/// <summary>
/// NetEase Cloud Music: timed lyrics for a huge catalog and the romaji ("romalrc") of many Japanese songs. The song is
/// searched once and both answers are kept for the next call. A match needs a similar title and a duration within three
/// seconds; without a known duration the artist has to match too.
/// </summary>
public sealed class NetEaseClient : ILyricsSource, IRomajiSource, IDisposable
{
    public string Name => "NetEase";

    public async Task<LyricsResult> Find(Track track)
    {
        if (await Lookup(track) is not { } entry)
            return Missing;
        if (entry.Instrumental)
            return new LyricsResult(true, true, false, "", Name);
        if (entry.Lrc.Trim().Length == 0)
            return Missing;

        return new LyricsResult(true, false, Lrc.isSynced(entry.Lrc), entry.Lrc, Name);
    }

    public async Task<string> FindRomaji(Track track) => (await Lookup(track))?.Romaji ?? "";

    public void Dispose() => _http.Dispose();

    private LyricsResult Missing => new(false, false, false, "", Name);

    private Task<Entry?> Lookup(Track track)
    {
        var key = track.Id;
        if (_memo.Count > 32)
            _memo.Clear();

        var task = _memo.GetOrAdd(key, _ => LookupAsync(track));
        _ = task.ContinueWith(_ => _memo.TryRemove(key, out _), TaskContinuationOptions.OnlyOnFaulted);
        return task;
    }

    private async Task<Entry?> LookupAsync(Track track)
    {
        var artist = (track.Artists is { Length: > 0 } list ? list[0] : track.Artist ?? "").Trim();
        var title = LyricTitle.shorten(track.Title ?? "");

        if (title.Length == 0)
            return null;

        var id = await SearchAsync(track, artist, title);
        if (id == 0)
            return null;

        using var doc = await GetAsync($"song/lyric?id={id}&lv=1&kv=1&tv=-1&rv=1");
        var root = doc.RootElement;

        return new Entry(Lyric(root, "lrc"), Lyric(root, "romalrc"), IsTrue(root, "nolyric"));
    }

    private async Task<long> SearchAsync(Track track, string artist, string title)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["s"] = (title + " " + artist).Trim(), ["type"] = "1", ["limit"] = "8", ["offset"] = "0"
        });

        using var response = await _http.PostAsync(Api + "search/get", content);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
            return 0;

        var wantTitle = Norm(title);
        var wantArtist = Norm(artist);
        long best = 0;
        var bestScore = double.MaxValue;

        foreach (var song in songs.EnumerateArray())
        {
            var name = Norm(LyricTitle.shorten(Str(song, "name")));

            if (name.Length == 0 || !(name.Contains(wantTitle) || wantTitle.Contains(name)))
                continue;

            var knownDuration = track.DurationSeconds > 0 && song.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number;
            var gap = knownDuration ? Math.Abs(Number(song, "duration") / 1000 - track.DurationSeconds) : 0;

            if (gap > DurationTolerance)
                continue;

            var artistHit = wantArtist.Length > 0 && song.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
                && artists.EnumerateArray().Any(a =>
                {
                    var n = Norm(Str(a, "name"));
                    return n.Length > 0 && (n.Contains(wantArtist) || wantArtist.Contains(n));
                });

            if (!knownDuration && !artistHit)
                continue;

            var score = gap - (artistHit ? 5 : 0) + (name == wantTitle ? 0 : 1);

            if (score < bestScore && song.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                (best, bestScore) = (id.GetInt64(), score);
        }

        return best;
    }

    private async Task<JsonDocument> GetAsync(string path)
    {
        using var response = await _http.GetAsync(Api + path);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static string Lyric(JsonElement root, string name) =>
        root.TryGetProperty(name, out var o) && o.ValueKind == JsonValueKind.Object ? Text(o, "lyric") : "";

    private static string Norm(string text) => new string([.. text.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

    #region Fields

    private sealed record Entry(string Lrc, string Romaji, bool Instrumental);

    private const string Api = "https://music.163.com/api/";
    private const double DurationTolerance = 3.0;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConcurrentDictionary<string, Task<Entry?>> _memo = new();

    public NetEaseClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(LrclibClient.UserAgent);
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "appver=2.0.2");
    }

    #endregion
}
