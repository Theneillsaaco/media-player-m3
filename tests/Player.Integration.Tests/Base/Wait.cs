using System.Diagnostics;

namespace Player.Integration.Tests.Base;

internal static class Wait
{
    public static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("condition not met in " + timeoutMs + " ms");

            await Task.Delay(5);
        }
    }
}