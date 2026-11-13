using System.Collections.Concurrent;
using System.Text.Json;
using Player.Core.Contracts;
using Player.Providers.YoutubeMusic;

namespace Player.Integration.Tests;

public class SidecarProtocolTests
{
    private static ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending(long id, out TaskCompletionSource<JsonElement> tcs)
    {
        tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var map = new ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>();
        map[id] = tcs;
        return map;
    }
 
    [Fact]
    public void A_request_is_one_json_object()
    {
        var line = SidecarProtocol.Request(7, "search", new { query = "x" });
        var root = JsonDocument.Parse(line).RootElement;
 
        Assert.Equal(7, root.GetProperty("id").GetInt64());
        Assert.Equal("search", root.GetProperty("method").GetString());
        Assert.Equal("x", root.GetProperty("params").GetProperty("query").GetString());
        Assert.DoesNotContain('\n', line);
    }
 
    [Fact]
    public async Task An_ok_reply_completes_the_waiting_call()
    {
        var pending = Pending(1, out var tcs);
 
        SidecarProtocol.Dispatch("""{"id":1,"ok":true,"result":{"name":"me"}}""", pending);
 
        var result = await tcs.Task;
        Assert.Equal("me", result.GetProperty("name").GetString());
        Assert.Empty(pending);
    }
 
    [Fact]
    public async Task An_error_reply_fails_the_call()
    {
        var pending = Pending(1, out var tcs);
 
        SidecarProtocol.Dispatch("""{"id":1,"ok":false,"error":"boom"}""", pending);
 
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tcs.Task);
        Assert.Equal("boom", ex.Message);
    }
 
    [Theory]
    [InlineData("auth_expired")]
    [InlineData("auth_required")]
    public async Task Auth_codes_become_AuthExpiredException(string code)
    {
        var pending = Pending(1, out var tcs);
 
        SidecarProtocol.Dispatch($$"""{"id":1,"ok":false,"error":"nope","code":"{{code}}"}""", pending);
 
        await Assert.ThrowsAsync<AuthExpiredException>(() => tcs.Task);
    }
 
    [Theory]
    [InlineData("not json")]
    [InlineData("""{"ok":true,"result":1}""")]
    [InlineData("""{"id":"x","ok":true,"result":1}""")]
    [InlineData("""{"id":99,"ok":true,"result":1}""")]
    public void Lines_that_are_not_for_anyone_are_ignored(string line)
    {
        var pending = Pending(1, out var tcs);
 
        SidecarProtocol.Dispatch(line, pending);
 
        Assert.False(tcs.Task.IsCompleted);
        Assert.Single(pending);
    }
}