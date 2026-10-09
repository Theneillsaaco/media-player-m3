using static System.IO.Directory;

namespace Player.Storage;

/// <summary>Where the player keeps its files, following the XDG directories.</summary>
public static class AppPaths
{
    public static string ConfigFile(string name) => Path.Combine(Home("XDG_CONFIG_HOME", ".config"), "player", name);
    
    public static string DataFile(string name) => Path.Combine(Home("XDG_DATA_HOME", ".local/share"), "player", name);

    private static string Home(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
    
    /// <summary>Creates the folder of a file (mode 700 when new) and returns the file path.</summary>
    internal static string Prepare(string file)
    {
        if (Path.GetDirectoryName(file) is { Length: > 0 } dir)
            CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        
        return file;
    }
}