using Player.App.Services;
using Player.Integration.Tests.Base;

namespace Player.Integration.Tests;

public class BackgroundWorkTests
{
    private static async Task DrainUntil(BackgroundWork work, Func<bool> done) =>
        await Wait.Until(() => { work.Drain(); return done(); });
 
    [Fact]
    public async Task A_result_is_applied_only_on_drain()
    {
        var work = new BackgroundWork();
        int? got = null;
 
        work.Run(Task.FromResult(5), v => got = v, _ => { });
 
        Assert.True(work.Busy);
        await DrainUntil(work, () => got is not null);
        Assert.Equal(5, got);
        Assert.False(work.Busy);
    }
 
    [Fact]
    public async Task A_failure_reaches_the_failure_callback_unwrapped()
    {
        var work = new BackgroundWork();
        Exception? error = null;
 
        work.Run(Task.FromException<int>(new InvalidOperationException("x")), _ => { }, e => error = e);
 
        await DrainUntil(work, () => error is not null);
        Assert.IsType<InvalidOperationException>(error);
        Assert.False(work.Busy);
    }
 
    [Fact]
    public async Task A_callback_that_throws_does_not_stop_the_others()
    {
        var work = new BackgroundWork();
        var second = false;
 
        work.Run(Task.FromResult(1), _ => throw new Exception("bad callback"), _ => { });
        work.Run(Task.FromResult(2), _ => second = true, _ => { });
 
        await DrainUntil(work, () => second);
        Assert.True(second);
    }
}