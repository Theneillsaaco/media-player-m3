using Player.App.Services;

using Player.Integration.Tests.Base;

namespace Player.Integration.Tests;

public class LookupCacheTests
{
     private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
 
    [Fact]
    public async Task A_request_fetches_once_and_keeps_the_value()
    {
        var calls = 0;
        var cache = new LookupCache<string, int>(k => { calls++; return Task.FromResult(k.Length); }, "test", Minute, 8);
 
        cache.Request("abc");
        await Wait.Until(() => cache.Version > 0);
        cache.Request("abc");
        await Task.Delay(30);
 
        Assert.True(cache.TryGet("abc", out var value));
        Assert.Equal(3, value);
        Assert.Equal(1, calls);
        Assert.False(cache.Busy);
    }
 
    [Fact]
    public async Task A_failed_lookup_is_not_repeated_straight_away()
    {
        var calls = 0;
        var cache = new LookupCache<string, int>(_ => { calls++; throw new InvalidOperationException("down"); }, "test", Minute, 8);
 
        cache.Request("k");
        await Wait.Until(() => cache.Version > 0);
        cache.Request("k");
        await Task.Delay(30);
 
        Assert.False(cache.TryGet("k", out _));
        Assert.Equal(1, calls);
    }
 
    [Fact]
    public async Task Keys_that_are_not_accepted_are_never_fetched()
    {
        var calls = 0;
        var cache = new LookupCache<string, int>(_ => { calls++; return Task.FromResult(1); }, "test", Minute, 8, accepts: k => k.Length == 2);
 
        cache.Request("abc");
        await Task.Delay(30);
 
        Assert.Equal(0, calls);
        Assert.False(cache.Pending("abc"));
    }
 
    [Fact]
    public async Task The_oldest_entry_goes_when_the_cache_is_full()
    {
        var cache = new LookupCache<string, int>(k => Task.FromResult(k.Length), "test", Minute, 2);
 
        foreach (var key in new[] { "a", "bb", "ccc" })
        {
            var version = cache.Version;
            cache.Request(key);
            await Wait.Until(() => cache.Version > version);
        }
 
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("bb", out _));
        Assert.True(cache.TryGet("ccc", out _));
    }
}