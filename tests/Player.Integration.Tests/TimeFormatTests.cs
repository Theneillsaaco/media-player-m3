using Player.App.Services;

namespace Player.Integration.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(59.9, "0:59")]
    [InlineData(600, "10:00")]
    [InlineData(3725, "62:05")]
    public void Clock_is_minutes_and_seconds(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Clock(seconds));
 
    [Fact]
    public void Zero_or_less_gives_the_fallback()
    {
        Assert.Equal("", TimeFormat.Clock(0));
        Assert.Equal("", TimeFormat.Clock(-3));
        Assert.Equal("0:00", TimeFormat.Clock(0, "0:00"));
    }
}