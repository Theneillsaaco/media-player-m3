using Player.Core.Domain;

namespace Player.App.Services;

internal static class TrackSource
{
    /// <summary>A link or an absolute path plays directly; anything else is a YouTube Music id.</summary>
    public static bool IsDirect(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith('/');
    
    public static string SourceOf(Track t) => 
        IsDirect(t.Id) ? t.Id : "https://music.youtube.com/watch?v=" + t.Id;
}