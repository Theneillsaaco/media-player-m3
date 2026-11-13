using System.Text.Json;
using Player.Providers.YoutubeMusic;

namespace Player.Integration.Tests;

public class TrackMapperTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
 
    [Fact]
    public void A_full_object_maps_every_field()
    {
        var track = TrackMapper.ToTrack(Json("""
                                             {"id":"abc","title":"T","artist":"A, B","artists":["A","B"],"album":"Al","seconds":123.5,"art":"u","kind":"song"}
                                             """));
 
        Assert.Equal("abc", track.Id);
        Assert.Equal("T", track.Title);
        Assert.Equal(["A", "B"], track.Artists);
        Assert.Equal("Al", track.Album);
        Assert.Equal(123.5, track.DurationSeconds);
        Assert.Equal("u", track.ArtUrl);
        Assert.False(track.Kind.IsVideo);
    }
 
    [Fact]
    public void Missing_fields_fall_back_to_empty_values()
    {
        var track = TrackMapper.ToTrack(Json("{}"));
 
        Assert.Equal("", track.Id);
        Assert.Empty(track.Artists);
        Assert.Equal(0.0, track.DurationSeconds);
    }
 
    [Fact]
    public void The_single_artist_stands_in_when_the_list_is_missing()
    {
        var track = TrackMapper.ToTrack(Json("""{"artist":"Solo"}"""));
 
        Assert.Equal(["Solo"], track.Artists);
    }
 
    [Fact]
    public void The_video_kind_is_recognised()
    {
        Assert.True(TrackMapper.ToTrack(Json("""{"kind":"video"}""")).Kind.IsVideo);
    }
 
    [Fact]
    public void Strings_skips_empty_and_non_string_entries()
    {
        var found = TrackMapper.Strings(Json("""{"a":["x","",1,null,"y"]}"""), "a");
 
        Assert.Equal(["x", "y"], found);
    }
 
    [Fact]
    public void Text_ignores_values_of_the_wrong_type()
    {
        Assert.Equal("", TrackMapper.Text(Json("""{"a":5}"""), "a"));
        Assert.Equal("", TrackMapper.Text(Json("""{"a":null}"""), "a"));
    }
}