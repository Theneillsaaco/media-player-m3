using Player.Core.Domain;

namespace Player.Integration.Tests.Base;

internal static class Samples
{
    public static Track Song(string id, string artist = "artist", double seconds = 100) =>
        new(id, "title " + id, artist, [artist], "album", seconds, "art-" + id, TrackKind.Song);
 
    public static Listen Listen(string title, long startedAt = 1000, bool chosen = true) =>
        new(startedAt, "artist", title, "album", 200, chosen);
}