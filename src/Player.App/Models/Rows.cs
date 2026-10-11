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
}