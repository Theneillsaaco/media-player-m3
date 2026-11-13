using Player.Storage;

namespace Player.Integration.Tests;

[Collection("environment")]
public class AppPathsTests
{
    private static void With(string variable, string value, Action check)
    {
        var old = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
 
        try { check(); }
        finally { Environment.SetEnvironmentVariable(variable, old); }
    }
 
    [Fact]
    public void Config_files_follow_XDG_CONFIG_HOME()
    {
        With("XDG_CONFIG_HOME", "/tmp/cfg", () =>
            Assert.Equal(Path.Combine("/tmp/cfg", "player", "lastfm.json"), AppPaths.ConfigFile("lastfm.json")));
    }
 
    [Fact]
    public void Data_and_cache_follow_their_variables()
    {
        With("XDG_DATA_HOME", "/tmp/data", () =>
            Assert.Equal(Path.Combine("/tmp/data", "player", "player.db"), AppPaths.DataFile("player.db")));
        With("XDG_CACHE_HOME", "/tmp/cache", () =>
            Assert.Equal(Path.Combine("/tmp/cache", "player", "audio"), AppPaths.CacheDir("audio")));
    }
 
    [Fact]
    public void Without_the_variable_the_home_folder_is_used()
    {
        With("XDG_CONFIG_HOME", "", () =>
        {
            var path = AppPaths.ConfigFile("x");
 
            Assert.EndsWith(Path.Combine(".config", "player", "x"), path);
        });
    }
}