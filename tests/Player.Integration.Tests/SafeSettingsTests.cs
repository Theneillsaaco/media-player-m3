using Player.App.Services;
using Player.Integration.Tests.Base;

namespace Player.Integration.Tests;

public class SafeSettingsTests
{
    [Fact]
    public void Without_a_database_reads_are_null_and_writes_do_nothing()
    {
        var settings = new SafeSettings(null);

        settings.Set("k", "v");
        settings.Remove("k");

        Assert.Null(settings.Get("k"));
    }

    [Fact]
    public void With_a_database_values_round_trip_and_can_be_removed()
    {
        using var temp = new TempDb();
        var settings = new SafeSettings(temp.Db);

        settings.Set("k", "v");
        Assert.Equal("v", settings.Get("k"));

        settings.Remove("k");
        Assert.Null(settings.Get("k"));
    }

    [Fact]
    public void A_broken_database_never_throws()
    {
        var temp = new TempDb();
        var settings = new SafeSettings(temp.Db);
        temp.Dispose(); // the connection is gone

        var read = Record.Exception(() => settings.Get("k"));
        var write = Record.Exception(() => settings.Set("k", "v"));
        var remove = Record.Exception(() => settings.Remove("k"));

        Assert.Null(read);
        Assert.Null(write);
        Assert.Null(remove);
        Assert.Null(settings.Get("k"));
    }
}
