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
            e.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0.0,
            Text(e, "art"),
            Text(e, "kind") == "video" ? TrackKind.Video : TrackKind.Song);
    }
    
    public static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array 
            ? [.. v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(s => s.Length > 0)] 
            : [];
    
    public static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; 
}