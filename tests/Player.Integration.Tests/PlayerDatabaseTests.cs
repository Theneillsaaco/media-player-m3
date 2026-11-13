using Player.Core.Domain;
using Player.Integration.Tests.Base;
using Player.Storage.Database;

namespace Player.Integration.Tests;

public class PlayerDatabaseTests : IDisposable
{
    public void Dispose() => _temp.Dispose();

    #region Settings

    [Fact]
    public void A_missing_setting_is_null()
    {
        Assert.Null(Db.GetSetting("nope"));
    }
 
    [Fact]
    public void Settings_round_trip_and_overwrite()
    {
        Db.SetSetting("k", "1");
        Db.SetSetting("k", "2");
 
        Assert.Equal("2", Db.GetSetting("k"));
    }
 
    [Fact]
    public void Removing_a_setting_forgets_it()
    {
        Db.SetSetting("k", "1");
        Db.RemoveSetting("k");
 
        Assert.Null(Db.GetSetting("k"));
    }
 
    [Fact]
    public void Data_survives_reopening_the_file()
    {
        Db.SetSetting("k", "kept");
        Db.Dispose();
 
        using var again = new PlayerDatabase(_temp.Path);
 
        Assert.Equal("kept", again.GetSetting("k"));
    }
    
    #endregion

    #region Scrobble Queue

    [Fact]
    public void Enqueue_keeps_only_the_newest_rows()
    {
        for (var i = 0; i < 5; i++)
            Db.Enqueue("lastfm", Samples.Listen("t" + i, startedAt: 1000 + i), keep: 3);
 
        var rows = Db.Peek("lastfm", 10);
 
        Assert.Equal(3, Db.Count("lastfm"));
        Assert.Equal(["t2", "t3", "t4"], rows.Select(r => r.Listen.Title));
    }
 
    [Fact]
    public void Peek_is_oldest_first_and_limited()
    {
        for (var i = 0; i < 4; i++)
            Db.Enqueue("lastfm", Samples.Listen("t" + i), keep: 100);
 
        Assert.Equal(["t0", "t1"], Db.Peek("lastfm", 2).Select(r => r.Listen.Title));
    }
 
    [Fact]
    public void Services_do_not_see_each_others_queue()
    {
        Db.Enqueue("a", Samples.Listen("x"), keep: 10);
 
        Assert.Equal(0, Db.Count("b"));
        Assert.Empty(Db.Peek("b", 10));
    }
 
    [Fact]
    public void Remove_deletes_only_the_given_ids()
    {
        Db.Enqueue("s", Samples.Listen("a"), 10);
        Db.Enqueue("s", Samples.Listen("b"), 10);
        Db.Enqueue("s", Samples.Listen("c"), 10);
 
        var rows = Db.Peek("s", 10);
        Db.Remove([rows[0].Id, rows[2].Id]);
 
        Assert.Equal(["b"], Db.Peek("s", 10).Select(r => r.Listen.Title));
    }
 
    [Fact]
    public void DeleteOlderThan_drops_listens_before_the_cutoff()
    {
        Db.Enqueue("s", Samples.Listen("old", startedAt: 100), 10);
        Db.Enqueue("s", Samples.Listen("new", startedAt: 900), 10);
 
        Db.DeleteOlderThan("s", 500);
 
        Assert.Equal(["new"], Db.Peek("s", 10).Select(r => r.Listen.Title));
    }
 
    [Fact]
    public void The_chosen_flag_is_kept()
    {
        Db.Enqueue("s", Samples.Listen("auto", chosen: false), 10);
 
        Assert.False(Db.Peek("s", 1)[0].Listen.Chosen);
    }

    #endregion

    #region History

    [Fact]
    public void History_is_newest_first_and_can_be_cleared()
    {
        Db.AddHistory(Samples.Listen("first", startedAt: 100), 95.5, "id1");
        Db.AddHistory(Samples.Listen("second", startedAt: 200), 100, "id2");
 
        var history = Db.GetHistory(10);
 
        Assert.Equal(["second", "first"], history.Select(h => h.Title));
        Assert.Equal(95.5, history[1].Listened);
        Assert.Equal("id1", history[1].TrackId);
 
        Db.ClearHistory();
        Assert.Empty(Db.GetHistory(10));
    }
 
    [Fact]
    public void History_respects_the_limit()
    {
        for (var i = 0; i < 5; i++)
            Db.AddHistory(Samples.Listen("t" + i, startedAt: i), 1, "id" + i);
 
        Assert.Equal(2, Db.GetHistory(2).Count);
    }

    #endregion

    #region Saved Queue

    [Fact]
    public void There_is_no_saved_queue_at_first()
    {
        Assert.Null(Db.LoadQueueState());
    }
 
    [Fact]
    public void The_saved_queue_round_trips()
    {
        var video = new Track("v1", "Sample Title", "artist", ["artist"], "Album", 180.0, "http://art", TrackKind.Video);
        Db.SaveQueueState([Samples.Song("a"), video], index: 1, seconds: 42.5, volume: 80, repeat: "all", shuffle: true);
 
        var saved = Db.LoadQueueState()!;
 
        Assert.Equal(["a", "v1"], saved.Items.Select(t => t.Id));
        Assert.True(saved.Items[1].Kind.IsVideo);
        Assert.Equal("artist", saved.Items[0].Artists[0]);
        Assert.Equal(1, saved.Index);
        Assert.Equal(42.5, saved.Seconds);
        Assert.Equal(80, saved.Volume);
        Assert.Equal("all", saved.Repeat);
        Assert.True(saved.Shuffle);
    }
 
    [Fact]
    public void SaveProgress_only_moves_position_and_volume()
    {
        Db.SaveQueueState([Samples.Song("a")], 0, 1, 50, "off", false);
 
        Db.SaveProgress(99, 10);
        var saved = Db.LoadQueueState()!;
 
        Assert.Equal(99, saved.Seconds);
        Assert.Equal(10, saved.Volume);
        Assert.Single(saved.Items);
    }

    #endregion

    #region Playlist Cache

    [Fact]
    public void Cached_tracks_keep_their_order_and_are_replaced_as_a_whole()
    {
        Db.SaveTracks("LM", [Samples.Song("a"), Samples.Song("b"), Samples.Song("c")]);
        Assert.Equal(["a", "b", "c"], Db.LoadTracks("LM").Select(t => t.Id));
 
        Db.SaveTracks("LM", [Samples.Song("z")]);
        Assert.Equal(["z"], Db.LoadTracks("LM").Select(t => t.Id));
    }
 
    [Fact]
    public void Lists_are_cached_separately_and_cleared_together()
    {
        Db.SaveTracks("one", [Samples.Song("a")]);
        Db.SaveTracks("two", [Samples.Song("b")]);
 
        Assert.Single(Db.LoadTracks("one"));
 
        Db.ClearTrackCache();
 
        Assert.Empty(Db.LoadTracks("one"));
        Assert.Empty(Db.LoadTracks("two"));
    }

    #endregion

    #region Lyrics Cache

    [Fact]
    public void Lyrics_are_cached_and_replaced()
    {
        Assert.Null(Db.GetLyrics("id"));
 
        Db.SaveLyrics("id", "plain", "LRCLIB", "words");
        Db.SaveLyrics("id", "lrc", "NetEase", "[00:01.00]words");
        var cached = Db.GetLyrics("id")!;
 
        Assert.Equal("lrc", cached.Kind);
        Assert.Equal("NetEase", cached.Source);
        Assert.Equal("[00:01.00]words", cached.Body);
        Assert.True(cached.FetchedAt > 0);
    }

    #endregion
    
    #region Fields

    private readonly TempDb _temp = new();
    private PlayerDatabase Db => _temp.Db;

    #endregion
}