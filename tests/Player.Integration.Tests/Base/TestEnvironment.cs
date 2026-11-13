using System.Runtime.CompilerServices;

namespace Player.Integration.Tests.Base;

internal static class TestEnvironment
{
    /// <summary>Runs before anything else: the logger must not write into the developer's real ~/.local/state.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("PLAYER_LOG", "error");
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", Directory.CreateTempSubdirectory("player-state-").FullName);
    }
}