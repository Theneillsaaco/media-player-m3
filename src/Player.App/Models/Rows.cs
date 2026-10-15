using Player.App.Services;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.Models;

/// <summary>Domain objects to the rows QML list models show.</summary>
internal static class Rows
{
    public static TrackRow FromTrack(Track t) => new()
    {
        VideoId = t.Id,
        Title = t.Title,
        Artist = t.Artist,
        Album = t.Album,
        Duration = TimeFormat.Clock(t.DurationSeconds),
        ArtUrl = t.ArtUrl
    };

    public static TrackRow FromPlaylist(PlaylistInfo p) => new()
    {
        VideoId = p.Id,
        Title = p.Title,
        Artist = p.Count switch { 0 => "", 1 => "1 song", var n => $"{n} songs" },
        ArtUrl = p.ArtUrl
    };

    public static EntityRow FromEntity(Entity e) => new()
    {
        Kind = e.Kind switch { EntityKind.Artist => "artist", EntityKind.Playlist => "playlist", _ => "album" },
        EntityId = e.Id,
        Title = e.Title,
        Subtitle = e.Subtitle,
        ArtUrl = e.ArtUrl
    };

    /// <summary>A song inside a shelf of Home.</summary>
    public static EntityRow FromTrackEntity(Track t)
    {
        var duration = TimeFormat.Clock(t.DurationSeconds);

        return new()
        {
            Kind = "track",
            EntityId = t.Id,
            Title = t.Title,
            Subtitle = duration.Length > 0 ? t.Artist + " · " + duration : t.Artist,
            ArtUrl = t.ArtUrl
        };
    }

    public static EntityRow Header(string title) => new() { Kind = "header", Title = title };
}
