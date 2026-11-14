using Player.App.Services;
using Player.Core.Contracts;
using Player.Integration.Tests.Base;
using Player.Storage.Database;

namespace Player.Integration.Tests;

public class LyricsServiceTests
{
    private static LyricsResult Synced(string text = "[00:01.00]hi", string source = "S") => new(true, false, true, text, source);
    private static LyricsResult Plain(string text = "hi", string source = "S") => new(true, false, false, text, source);
    private static LyricsResult NotFound => new(false, false, false, "", "S");

    private static LyricsHarness With(params ILyricsSource[] sources) => new(sources);

    // ---- what is looked up ----

    [Fact]
    public void Asking_for_no_track_clears_the_lyrics()
    {
        var h = With(FakeSource.Returns(Synced()));

        h.Service.Request(null);

        Assert.Equal(LyricsStage.Idle, h.Service.Snapshot.Stage);
        Assert.Equal("", h.Service.Snapshot.TrackId);
    }

    [Theory]
    [InlineData("/music/a.mp3")]
    [InlineData("https://example.com/a.mp3")]
    [InlineData("")]
    public void Links_files_and_empty_ids_have_no_lyrics(string id)
    {
        var source = FakeSource.Returns(Synced());
        var h = With(source);

        var original = Samples.Song("x");
        var modifiedTrack = new Player.Core.Domain.Track(
            id, 
            original.Title, 
            original.Artist, 
            original.Artists, 
            original.Album, 
            original.DurationSeconds, 
            original.ArtUrl, 
            original.Kind
        );

        h.Service.Request(modifiedTrack);

        Assert.Equal(LyricsStage.Unavailable, h.Service.Snapshot.Stage);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_timed_result_is_published_and_cached()
    {
        var h = With(FakeSource.Returns(Synced("[00:01.00]hi", "LRCLIB")));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Synced, snapshot.Stage);
        Assert.Equal("[00:01.00]hi", snapshot.Text);
        Assert.Equal("LRCLIB", snapshot.Source);
        Assert.Equal(("abc", "lrc", "LRCLIB", "[00:01.00]hi"), Assert.Single(h.Writes));
    }

    [Fact]
    public async Task Timed_lyrics_from_a_later_source_beat_plain_ones_from_an_earlier_one()
    {
        var second = FakeSource.Returns(Synced());
        var h = With(FakeSource.Returns(Plain()), second);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Synced, snapshot.Stage);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task Plain_lyrics_are_kept_when_nothing_is_timed()
    {
        var h = With(FakeSource.Returns(Plain("words")), FakeSource.Returns(NotFound));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Plain, snapshot.Stage);
        Assert.Equal("plain", Assert.Single(h.Writes).Kind);
    }

    [Fact]
    public async Task An_instrumental_track_is_reported_and_cached()
    {
        var h = With(FakeSource.Returns(new LyricsResult(true, true, false, "", "S")));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Instrumental, snapshot.Stage);
        Assert.Equal("instrumental", Assert.Single(h.Writes).Kind);
    }

    [Fact]
    public async Task Nothing_found_anywhere_is_remembered_as_none()
    {
        var h = With(FakeSource.Returns(NotFound), FakeSource.Returns(NotFound));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.None, snapshot.Stage);
        Assert.Equal("none", Assert.Single(h.Writes).Kind);
    }

    [Fact]
    public async Task When_no_source_can_be_reached_it_is_an_error_and_nothing_is_cached()
    {
        var h = With(FakeSource.Throws());

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Error, snapshot.Stage);
        Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task One_source_down_does_not_matter_when_another_has_the_lyrics()
    {
        var h = With(FakeSource.Throws("down"), FakeSource.Returns(Synced()));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Synced, snapshot.Stage);
    }

    // ---- the cache ----

    [Fact]
    public async Task A_cached_answer_skips_every_source()
    {
        var source = FakeSource.Returns(Synced());
        var h = With(source);
        h.Cache["abc"] = new CachedLyrics("lrc", "LRCLIB", "[00:01.00]cached", h.Now);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Synced, snapshot.Stage);
        Assert.Equal("[00:01.00]cached", snapshot.Text);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_recent_none_is_trusted()
    {
        var source = FakeSource.Returns(Synced());
        var h = With(source);
        h.Cache["abc"] = new CachedLyrics("none", "", "", h.Now - 3600);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.None, snapshot.Stage);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_none_older_than_a_week_is_looked_up_again()
    {
        var source = FakeSource.Returns(Synced());
        var h = With(source);
        h.Cache["abc"] = new CachedLyrics("none", "", "", h.Now - 8 * 86_400);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Synced, snapshot.Stage);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task A_cached_kind_this_version_does_not_know_is_looked_up_again()
    {
        var source = FakeSource.Returns(Plain());
        var h = With(source);
        h.Cache["abc"] = new CachedLyrics("from-the-future", "", "x", h.Now);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Plain, snapshot.Stage);
        Assert.Equal(1, source.Calls);
    }

    // ---- asking again ----

    [Fact]
    public async Task Asking_again_for_the_same_track_does_nothing_unless_it_is_forced()
    {
        var source = FakeSource.Returns(Synced());
        var h = With(source);
        var track = Samples.Song("abc");
        var first = await h.Ask(track);

        h.Service.Request(track);
        Assert.Equal(first.Version, h.Service.Snapshot.Version);

        await h.Ask(track, force: true);
        Assert.True(h.Service.Snapshot.Version > first.Version);
    }

    [Fact]
    public async Task A_failed_lookup_is_asked_again_without_forcing()
    {
        var calls = 0;
        var source = new FakeSource("S", _ => ++calls == 1
            ? throw new HttpRequestException("down")
            : Task.FromResult(Synced()));
        var h = With(source);
        var track = Samples.Song("abc");

        Assert.Equal(LyricsStage.Error, (await h.Ask(track)).Stage);
        Assert.Equal(LyricsStage.Synced, (await h.Ask(track)).Stage);
    }

    [Fact]
    public async Task A_newer_request_owns_the_answer()
    {
        var gate = new TaskCompletionSource<LyricsResult>();
        var source = new FakeSource("S", t => t.Id == "slowslowslo" ? gate.Task : Task.FromResult(Synced()));
        var h = With(source);

        h.Service.Request(Samples.Song("slowslowslo"));
        await h.Ask(Samples.Song("fastfastfas"));

        gate.SetResult(Plain("late"));
        await Wait.Until(() => h.Writes.Count == 2);
        await Task.Delay(100); // the stale answer has had every chance to overwrite the snapshot

        Assert.Equal("fastfastfas", h.Service.Snapshot.TrackId);
        Assert.Equal(LyricsStage.Synced, h.Service.Snapshot.Stage);
    }

    // ---- preferences ----

    [Fact]
    public void The_text_size_has_a_default_and_stays_within_its_limits()
    {
        var h = With();
        Assert.Equal(LyricsService.DefaultFontSize, h.Service.FontSize);

        h.Service.SaveFontSize(500);
        Assert.Equal(LyricsService.MaxFontSize, h.Service.FontSize);

        h.Service.SaveFontSize(1);
        Assert.Equal(LyricsService.MinFontSize, h.Service.FontSize);

        h.Service.SaveFontSize(26);
        Assert.Equal(26, h.Service.FontSize);
    }

    [Fact]
    public void The_delay_is_per_track_and_zero_forgets_it()
    {
        var h = With();
        Assert.Equal(0, h.Service.Delay("abc"));

        h.Service.SaveDelay("abc", 1.5);
        Assert.Equal(1.5, h.Service.Delay("abc"));
        Assert.Equal(0, h.Service.Delay("other"));

        h.Service.SaveDelay("abc", 0);
        Assert.Empty(h.Settings);
        Assert.Equal(0, h.Service.Delay("abc"));
    }

    [Fact]
    public void A_stored_delay_is_clamped_and_junk_reads_as_zero()
    {
        var h = With();

        h.Settings["lyrics.delay:abc"] = "99";
        h.Settings["lyrics.delay:bad"] = "soon";

        Assert.Equal(15, h.Service.Delay("abc"));
        Assert.Equal(0, h.Service.Delay("bad"));
    }

    [Fact]
    public void The_empty_track_has_no_delay_to_save()
    {
        var h = With();

        h.Service.SaveDelay("", 2);

        Assert.Empty(h.Settings);
    }

    [Fact]
    public void The_extra_text_mode_defaults_to_off_and_ignores_junk()
    {
        var h = With();
        Assert.Equal("off", h.Service.Extra);

        h.Service.SaveExtra("both");
        Assert.Equal("both", h.Service.Extra);

        h.Service.SaveExtra("junk");
        Assert.Equal("off", h.Service.Extra);
    }

    // ---- romaji and translation ----

    [Fact]
    public async Task Extras_are_not_asked_for_while_the_mode_is_off()
    {
        var romaji = new FakeRomaji("[00:10.00]x");
        var translator = new FakeTranslator(l => l);
        var h = new LyricsHarness([FakeSource.Returns(Synced("[00:10.00]a"))], [romaji], translator);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Null(snapshot.Romaji);
        Assert.Null(snapshot.Translation);
        Assert.Equal(0, romaji.Calls + translator.Calls);
    }

    [Fact]
    public async Task Romaji_is_paired_with_the_lines_by_time()
    {
        var romaji = new FakeRomaji("[00:10.20]konnichiwa\n[00:20.10]sayounara");
        var h = new LyricsHarness([FakeSource.Returns(Synced("[00:10.00]こんにちは\n[00:20.00]さようなら"))], [romaji]);
        h.Settings["lyrics.extra"] = "romaji";

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(["konnichiwa", "sayounara"], snapshot.Romaji);
        Assert.Null(snapshot.Translation);
    }

    [Fact]
    public async Task Romaji_is_cached_for_the_next_time()
    {
        var romaji = new FakeRomaji("[00:10.20]konnichiwa");
        var h = new LyricsHarness([FakeSource.Returns(Synced("[00:10.00]こんにちは"))], [romaji]);
        h.Settings["lyrics.extra"] = "romaji";
        var track = Samples.Song("abc");

        await h.Ask(track);
        var again = await h.Ask(track, force: true);

        Assert.Equal(["konnichiwa"], again.Romaji);
        Assert.Equal(1, romaji.Calls);
    }

    [Fact]
    public async Task A_translation_blanks_the_lines_that_come_back_unchanged()
    {
        var translator = new FakeTranslator(_ => ["one", "DOS"]);
        var h = new LyricsHarness([FakeSource.Returns(Plain("one\ntwo"))], translator: translator);
        h.Settings["lyrics.extra"] = "translation";

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(["", "DOS"], snapshot.Translation);
    }

    [Fact]
    public async Task A_translation_is_cached_for_the_next_time()
    {
        var translator = new FakeTranslator(_ => ["UNO", "DOS"]);
        var h = new LyricsHarness([FakeSource.Returns(Plain("one\ntwo"))], translator: translator);
        h.Settings["lyrics.extra"] = "translation";
        var track = Samples.Song("abc");

        await h.Ask(track);
        var again = await h.Ask(track, force: true);

        Assert.Equal(["UNO", "DOS"], again.Translation);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task A_failing_translator_never_hides_the_lyrics()
    {
        var translator = new FakeTranslator(_ => throw new HttpRequestException("limit"));
        var h = new LyricsHarness([FakeSource.Returns(Plain("one\ntwo"))], translator: translator);
        h.Settings["lyrics.extra"] = "translation";

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Plain, snapshot.Stage);
        Assert.Null(snapshot.Translation);
    }

    [Fact]
    public async Task Extras_are_skipped_for_tracks_without_lines()
    {
        var translator = new FakeTranslator(l => l);
        var h = new LyricsHarness([FakeSource.Returns(NotFound)], translator: translator);
        h.Settings["lyrics.extra"] = "both";

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.None, snapshot.Stage);
        Assert.Equal(0, translator.Calls);
    }
}