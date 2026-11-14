using Player.App.Services;
using Player.Core.Contracts;
using Player.Integration.Tests.Base;

namespace Player.Integration.Tests;

/// <summary>The snapshot carries the lyrics already parsed, so the view model never parses the text again.</summary>
public class LyricsSnapshotLinesTests
{
    private static LyricsHarness With(LyricsResult result) => new([FakeSource.Returns(result)]);

    [Fact]
    public async Task Timed_lyrics_come_parsed_and_in_time_order()
    {
        var h = With(new LyricsResult(true, false, true, "[00:20.00]second\n[00:10.00]first", "S"));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.NotNull(snapshot.Lines);
        Assert.Equal(["first", "second"], snapshot.Lines!.Select(l => l.Text));
        Assert.Equal([10.0, 20.0], snapshot.Lines!.Select(l => l.Start));
    }

    [Fact]
    public async Task Plain_lyrics_come_as_untimed_lines()
    {
        var h = With(new LyricsResult(true, false, false, "one\n\n\n\ntwo", "S"));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(["one", "", "two"], snapshot.Lines!.Select(l => l.Text));
        Assert.All(snapshot.Lines!, l => Assert.Equal(-1.0, l.Start));
    }

    [Fact]
    public async Task Stages_without_lyrics_have_no_lines()
    {
        var none = await With(new LyricsResult(false, false, false, "", "S")).Ask(Samples.Song("abc"));
        var instrumental = await With(new LyricsResult(true, true, false, "", "S")).Ask(Samples.Song("abc"));

        Assert.Empty(none.Lines ?? []);
        Assert.Empty(instrumental.Lines ?? []);
    }

    [Fact]
    public async Task A_failed_lookup_has_no_lines()
    {
        var h = new LyricsHarness([FakeSource.Throws()]);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Error, snapshot.Stage);
        Assert.Empty(snapshot.Lines ?? []);
    }

    [Fact]
    public async Task Lines_from_the_cache_are_parsed_too()
    {
        var source = FakeSource.Returns(new LyricsResult(true, false, true, "[00:01.00]x", "S"));
        var h = new LyricsHarness([source]);
        h.Cache["abc"] = new("lrc", "LRCLIB", "[00:05.00]cached", h.Now);

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(["cached"], snapshot.Lines!.Select(l => l.Text));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_synced_text_with_nothing_usable_has_empty_lines()
    {
        var h = With(new LyricsResult(true, false, true, "no marks here", "S"));

        var snapshot = await h.Ask(Samples.Song("abc"));

        Assert.Equal(LyricsStage.Synced, snapshot.Stage); // the view model turns this into "none"
        Assert.Empty(snapshot.Lines!);
    }
}