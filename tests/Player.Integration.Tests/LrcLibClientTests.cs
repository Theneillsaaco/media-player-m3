using System.Net;
using System.Text;
using Player.Core.Domain;
using Player.Integration.Tests.Base;
using Player.Providers.Lyrics;

namespace Player.Integration.Tests;

public class LrcLibClientTests
{
     private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
 
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }
 
    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
 
    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
 
    private static LrclibClient Client(FakeHandler handler) => new(new HttpClient(handler));
 
    [Fact]
    public async Task The_exact_match_is_used_first()
    {
        var handler = new FakeHandler(_ => Json("""{"duration":100,"syncedLyrics":"[00:01.00]hi","plainLyrics":"hi"}"""));
 
        var result = await Client(handler).Find(Samples.Song("a"));
 
        Assert.True(result.Found);
        Assert.True(result.Synced);
        Assert.Equal("[00:01.00]hi", result.Text);
        Assert.Equal(["/api/get"], handler.Paths);
    }
 
    [Fact]
    public async Task Search_prefers_timed_lyrics_over_a_closer_plain_one()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath == "/api/get"
            ? NotFound()
            : Json("""
                   [{"duration":100,"plainLyrics":"plain"},
                    {"duration":101.5,"syncedLyrics":"[00:02.00]timed"}]
                   """));
 
        var result = await Client(handler).Find(Samples.Song("a"));
 
        Assert.True(result.Synced);
        Assert.Equal("[00:02.00]timed", result.Text);
    }
 
    [Fact]
    public async Task Lyrics_of_another_version_are_rejected()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath == "/api/get"
            ? NotFound()
            : Json("""[{"duration":160,"syncedLyrics":"[00:02.00]other version"}]"""));
 
        var result = await Client(handler).Find(Samples.Song("a"));
 
        Assert.False(result.Found);
    }
 
    [Fact]
    public async Task An_instrumental_track_is_reported_as_such()
    {
        var handler = new FakeHandler(_ => Json("""{"duration":100,"instrumental":true}"""));
 
        var result = await Client(handler).Find(Samples.Song("a"));
 
        Assert.True(result.Found);
        Assert.True(result.Instrumental);
    }
 
    [Fact]
    public async Task A_track_without_artist_asks_nothing()
    {
        var handler = new FakeHandler(_ => NotFound());
 
        var baseSong = Samples.Song("a");
        var modifiedSong = new Track(
            baseSong.Id, 
            baseSong.Title, 
            "", 
            [], 
            baseSong.Album, 
            baseSong.DurationSeconds, 
            baseSong.ArtUrl, 
            baseSong.Kind
        );

        var result = await Client(handler).Find(modifiedSong);
        
        Assert.False(result.Found);
        Assert.Empty(handler.Paths);
    }
 
    [Fact]
    public async Task A_server_error_is_not_mistaken_for_no_lyrics()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
 
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).Find(Samples.Song("a")));
    }
}