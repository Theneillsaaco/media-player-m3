using System.Globalization;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Core.Localization;
using Player.Core.Lyrics;
using Player.Storage.Database;

namespace Player.App.Services;

internal enum LyricsStage { Idle, Loading, Synced, Plain, Instrumental, None, Error, Unavailable }

/// <summary>
/// What is known about the lyrics of one track. Version goes up with every change, so a poll can tell. Lines are the
/// lyrics already parsed (timed for Synced, Start = -1 for Plain, null for every other stage), so nobody parses the
/// text again.
/// </summary>
internal sealed record LyricsSnapshot(string TrackId, LyricsStage Stage, string Text, string Source, int Version,
    string[]? Romaji = null, string[]? Translation = null, LyricLine[]? Lines = null);

/// <summary>
/// Finds the lyrics of a track: the local cache first, then each source in order, keeping the best (timed beats
/// plain). The result, even "no lyrics", is cached so the network is not asked twice; a "none" is retried after a
/// week. When a source cannot be reached and nothing was found, the answer is an error and nothing is cached.
/// Requests run on the thread pool; the UI reads <see cref="Snapshot"/> from its poll. Also keeps the small
/// per-track and global lyrics preferences (delay, text size).
/// </summary>
internal sealed class LyricsService(
    IReadOnlyList<ILyricsSource> sources,
    Func<string, CachedLyrics?> readCache,
    Action<string, string, string, string> writeCache,
    Func<string, string?> readSetting,
    Action<string, string> writeSetting,
    Action<string> removeSetting,
    IReadOnlyList<IRomajiSource>? romajiSources = null,
    ILineTranslator? translator = null,
    Func<long>? clock = null)
{
    public const int MinFontSize = 14;
    public const int MaxFontSize = 40;
    public const int DefaultFontSize = 20;

    private static readonly long NoneRetrySeconds = (long)TimeSpan.FromDays(7).TotalSeconds;
    private const string SizeKey = "lyrics.size";

    private readonly Lock _gate = new();
    private LyricsSnapshot _snapshot = new("", LyricsStage.Idle, "", "", 0);
    private int _ticket;

    public LyricsSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    /// <summary>Starts looking for the lyrics of a track (or clears them for null). Asking again for the same track does nothing, unless it failed or <paramref name="force"/> is set.</summary>
    public void Request(Track? track, bool force = false)
    {
        int ticket;

        lock (_gate)
        {
            if (track is null)
            {
                _ticket++;
                Publish("", LyricsStage.Idle);
                return;
            }

            if (track.Id.Length == 0 || TrackSource.IsDirect(track.Id))
            {
                _ticket++;
                Publish(track.Id, LyricsStage.Unavailable);
                return;
            }

            if (!force && _snapshot.TrackId == track.Id && _snapshot.Stage != LyricsStage.Error)
                return;

            ticket = ++_ticket;
            Publish(track.Id, LyricsStage.Loading);
        }

        _ = Task.Run(() => LoadAsync(track, ticket));
    }

    // ---- preferences ----

    /// <summary>How many seconds later than the music the lyrics of this track are shown (negative: sooner).</summary>
    public double Delay(string trackId)
    {
        if (trackId.Length == 0 || ReadSetting(DelayKey(trackId)) is not { } text)
            return 0;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, -LyricsSync.maxDelay, LyricsSync.maxDelay)
            : 0;
    }

    public void SaveDelay(string trackId, double seconds)
    {
        if (trackId.Length == 0)
            return;

        if (seconds == 0)
            Write(() => removeSetting(DelayKey(trackId)));
        else
            Write(() => writeSetting(DelayKey(trackId), seconds.ToString("0.0", CultureInfo.InvariantCulture)));
    }

    public int FontSize =>
        int.TryParse(ReadSetting(SizeKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
            ? Math.Clamp(size, MinFontSize, MaxFontSize)
            : DefaultFontSize;

    public void SaveFontSize(int size) =>
        Write(() => writeSetting(SizeKey, Math.Clamp(size, MinFontSize, MaxFontSize).ToString(CultureInfo.InvariantCulture)));

    // ---- lookup ----

    private async Task LoadAsync(Track track, int ticket)
    {
        (LyricsStage Stage, string Text, string Source) result;
        LyricLine[] lines = [];
        string[]? romaji = null, translation = null;

        try
        {
            result = await ResolveAsync(track);
            lines = LinesOf(result.Stage, result.Text); // parsed once: the extras and the view model both use it
            (romaji, translation) = await ExtrasAsync(track, result.Stage, lines);
        }
        catch (Exception ex)
        {
            Log.Warn("lyrics", "lookup failed for " + track.Id, ex);
            result = (LyricsStage.Error, "", "");
            lines = [];
        }

        lock (_gate)
        {
            if (ticket == _ticket) // a newer request owns the answer
                Publish(track.Id, result.Stage, result.Text, result.Source, romaji, translation, lines);
        }
    }

    /// <summary>The lyrics as lines: timed for a synced text, untimed for a plain one, none for any other stage.</summary>
    private static LyricLine[] LinesOf(LyricsStage stage, string text) => stage switch
    {
        LyricsStage.Synced => Lrc.parse(text),
        LyricsStage.Plain => Lrc.plain(text),
        _ => [],
    };

    private async Task<(LyricsStage Stage, string Text, string Source)> ResolveAsync(Track track)
    {
        if (Cached(track.Id) is { } cached)
            return cached;

        LyricsResult? synced = null;
        LyricsResult? plain = null;
        var instrumental = false;
        var failures = 0;

        foreach (var source in sources)
        {
            try
            {
                var found = await source.Find(track);

                if (!found.Found)
                    continue;
                if (found.Instrumental)
                    instrumental = true;
                else if (found.Synced)
                {
                    synced = found;
                    break; // timed lyrics are the best there is
                }
                else
                    plain ??= found;
            }
            catch (Exception ex)
            {
                failures++;
                Log.Warn("lyrics", $"{source.Name} failed for {track.Id}", ex);
            }
        }

        if (synced is not null)
            return Keep(track.Id, "lrc", LyricsStage.Synced, synced);
        if (plain is not null)
            return Keep(track.Id, "plain", LyricsStage.Plain, plain);
        if (instrumental)
            return Keep(track.Id, "instrumental", LyricsStage.Instrumental, new LyricsResult(true, true, false, "", ""));
        if (failures > 0)
            return (LyricsStage.Error, "", ""); // could not ask everywhere: do not remember "none"

        return Keep(track.Id, "none", LyricsStage.None, new LyricsResult(false, false, false, "", ""));
    }

    private (LyricsStage, string, string)? Cached(string trackId)
    {
        var row = ReadCache(trackId);

        return row switch
        {
            null => null,
            { Kind: "lrc" } => (LyricsStage.Synced, row.Body, row.Source),
            { Kind: "plain" } => (LyricsStage.Plain, row.Body, row.Source),
            { Kind: "instrumental" } => (LyricsStage.Instrumental, "", row.Source),
            { Kind: "none" } when Now() - row.FetchedAt < NoneRetrySeconds => (LyricsStage.None, "", ""),
            _ => null, // an old "none", or a kind this version does not know: look again
        };
    }

    private (LyricsStage, string, string) Keep(string trackId, string kind, LyricsStage stage, LyricsResult result)
    {
        Write(() => writeCache(trackId, kind, result.Source, result.Text));
        return (stage, result.Text, result.Source);
    }

    private void Publish(string trackId, LyricsStage stage, string text = "", string source = "",
        string[]? romaji = null, string[]? translation = null, LyricLine[]? lines = null) =>
        _snapshot = new LyricsSnapshot(trackId, stage, text, source, _snapshot.Version + 1, romaji, translation, lines);

    private long Now() => clock?.Invoke() ?? Clock.UnixNow();

    private static string DelayKey(string trackId) => "lyrics.delay:" + trackId;

    private string? ReadSetting(string key)
    {
        try { return readSetting(key); }
        catch (Exception ex)
        {
            Log.Warn("lyrics", "setting " + key, ex);
            return null;
        }
    }

    private static void Write(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Warn("lyrics", "storage", ex); }
    }

    // ---- romaji and translation ----

    private const string ExtraKey = "lyrics.extra";

    /// <summary>What is shown under each line: off, romaji, translation or both.</summary>
    public string Extra
    {
        get
        {
            var value = ReadSetting(ExtraKey);
            return value is "romaji" or "translation" or "both" ? value : "off";
        }
    }

    public void SaveExtra(string mode) =>
        Write(() => writeSetting(ExtraKey, mode is "romaji" or "translation" or "both" ? mode : "off"));

    /// <summary>Never throws: the lyrics are shown even when the extras fail. Results are cached under "romaji:id" and "tr:lang:id".</summary>
    private async Task<(string[]? Romaji, string[]? Translation)> ExtrasAsync(Track track, LyricsStage stage, LyricLine[] lines)
    {
        var mode = Extra;
        if (mode == "off" || stage is not (LyricsStage.Synced or LyricsStage.Plain))
            return (null, null);

        string[]? romaji = null, translation = null;

        if (stage == LyricsStage.Synced && mode is "romaji" or "both")
            romaji = await Guarded(() => RomajiAsync(track, lines), "romaji");
        if (mode is "translation" or "both")
            translation = await Guarded(() => TranslationAsync(track, lines), "translation");

        return (romaji, translation);
    }

    private static async Task<string[]?> Guarded(Func<Task<string[]?>> work, string what)
    {
        try { return await work(); }
        catch (Exception ex)
        {
            Log.Warn("lyrics", what + " failed", ex);
            return null;
        }
    }

    private async Task<string[]?> RomajiAsync(Track track, LyricLine[] lines)
    {
        if (romajiSources is not { Count: > 0 })
            return null;

        var key = "romaji:" + track.Id;
        var body = "";
        var row = ReadCache(key);

        if (row is { Kind: "lrc" })
            body = row.Body;
        else if (!(row is { Kind: "none" } && Now() - row.FetchedAt < NoneRetrySeconds))
        {
            var failed = false;

            foreach (var source in romajiSources)
            {
                try { body = await source.FindRomaji(track); }
                catch (Exception ex)
                {
                    failed = true;
                    Log.Warn("lyrics", "romaji source failed for " + track.Id, ex);
                }

                if (body.Length > 0)
                    break;
            }

            if (body.Length > 0)
                Write(() => writeCache(key, "lrc", "romaji", body));
            else if (!failed)
                Write(() => writeCache(key, "none", "", ""));
        }

        if (body.Length == 0)
            return null;

        var paired = LyricsExtras.pair(lines, Lrc.parse(body));
        return paired.Any(p => p.Length > 0) ? paired : null;
    }

    private async Task<string[]?> TranslationAsync(Track track, LyricLine[] lines)
    {
        if (translator is null || lines.Length == 0)
            return null;

        var language = I18n.current();
        var key = $"tr:{language}:{track.Id}";

        if (ReadCache(key) is { Kind: "plain" } row && row.Body.Split('\n') is var cached && cached.Length == lines.Length)
            return cached.Any(t => t.Length > 0) ? cached : null;

        var result = await translator.Translate([.. lines.Select(l => l.Text)], language);

        for (var i = 0; i < result.Length; i++)
            if (string.Equals(result[i].Trim(), lines[i].Text.Trim(), StringComparison.OrdinalIgnoreCase))
                result[i] = ""; // already in that language

        Write(() => writeCache(key, "plain", "translate", string.Join("\n", result)));
        return result.Any(t => t.Length > 0) ? result : null;
    }

    private CachedLyrics? ReadCache(string key)
    {
        try { return readCache(key); }
        catch (Exception ex)
        {
            Log.Warn("lyrics", "cache read", ex);
            return null;
        }
    }
}
