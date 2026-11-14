using System.Text.Json;
using Player.Core.Domain;

namespace Player.Providers.YoutubeMusic;

/// <summary>Turns the sidecar's JSON objects into domain types.</summary>
internal static class TrackMapper
{
    public static Track ToTrack(JsonElement e)
    {
        var artist = Text(e, "artist");
        var artists = Strings(e, "artists");

        return new Track(
            Text(e, "id"),
            Text(e, "title"),
            artist,
            artists.Length > 0 ? artists : artist.Length > 0 ? [artist] : [],
            Text(e, "album"),
            JsonRead.Number(e, "seconds"),
            Text(e, "art"),
            Text(e, "kind") == "video" ? TrackKind.Video : TrackKind.Song);
    }

    // Kept so the provider files can keep their `using static TrackMapper`.
    public static string[] Strings(JsonElement e, string name) => JsonRead.Strings(e, name);

    public static string Text(JsonElement e, string name) => JsonRead.Text(e, name);
}