using System.Text.Json;
using Player.Core.Contracts;
using Player.Core.Domain;
using static Player.Providers.YoutubeMusic.TrackMapper;

namespace Player.Providers.YoutubeMusic;

// The browsing half of the provider (home, album, artist, typed search...). YtSidecarProvider is partial so this
// lives next to the feature and shares its process and its saved cookie.
public partial class YtSidecarProvider : IBrowseProvider
{
    /// <summary>Home shelves: personalized when a session is saved, generic otherwise (or when Google rejects it).</summary>
    public async Task<Shelf[]> Home()
    {
        bool signed;
        lock (_gate) signed = _cookie is not null;

        JsonElement result;
        try
        {
            result = await CallAsync("home", new { limit = 4 }, SidecarProtocol.ResolveTimeout, signedIn: signed);
        }
        catch (AuthExpiredException) when (signed)
        {
            result = await CallAsync("home", new { limit = 4, anonymous = true }, SidecarProtocol.ResolveTimeout);
        }

        return
        [
            .. result.EnumerateArray()
                .Select(s => new Shelf(Text(s, "title"), ReadTracks(s.GetProperty("items")), ReadEntities(s.GetProperty("items"))))
                .Where(s => s.Tracks.Length + s.Entities.Length > 0)
        ];
    }

    public async Task<TrackCollection> Album(string id) =>
        ToCollection(await CallAsync("album", new { id }, SidecarProtocol.ResolveTimeout));

    public async Task<TrackCollection> Collection(string id) =>
        ToCollection(await CallAsync("collection", new { id }, SidecarProtocol.ResolveTimeout));

    public async Task<ArtistPage> Artist(string id)
    {
        var r = await CallAsync("artist", new { id }, SidecarProtocol.ResolveTimeout);

        return new ArtistPage(
            Text(r, "id"),
            Text(r, "name"),
            Text(r, "description"),
            Text(r, "art"),
            Text(r, "songsId"),
            [.. r.GetProperty("songs").EnumerateArray().Select(ToTrack)],
            ReadEntities(r.GetProperty("albums")),
            ReadEntities(r.GetProperty("singles")),
            ReadEntities(r.GetProperty("related")));
    }

    public async Task<SearchHits> SearchTyped(string query, string filter)
    {
        var r = await CallAsync("search_typed", new { query, filter, limit = 30 });
        return new SearchHits(ReadTracks(r), ReadEntities(r));
    }

    public async Task<string[]> Suggestions(string query)
    {
        var r = await CallAsync("search_suggestions", new { query });
        return [.. r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)];
    }

    public async Task<TrackLinks> Links(string trackId)
    {
        var r = await CallAsync("track_links", new { id = trackId });
        return new TrackLinks(Text(r, "artistId"), Text(r, "artistName"), Text(r, "albumId"), Text(r, "albumName"));
    }

    private static TrackCollection ToCollection(JsonElement r) =>
        new(
            Text(r, "id"),
            Text(r, "title"),
            Text(r, "subtitle"),
            Text(r, "art"),
            Text(r, "artistId"),
            [.. r.GetProperty("tracks").EnumerateArray().Select(ToTrack)]);

    /// <summary>The tracks of a mixed list ({"type":"track","track":{...}} entries).</summary>
    private static Track[] ReadTracks(JsonElement list) =>
        [.. list.EnumerateArray().Where(i => i.TryGetProperty("track", out _)).Select(i => ToTrack(i.GetProperty("track")))];

    private static Entity[] ReadEntities(JsonElement list) =>
        [.. list.EnumerateArray().Select(ToEntity).OfType<Entity>()];

    /// <summary>Null for a track entry or a kind the player has no page for.</summary>
    private static Entity? ToEntity(JsonElement e)
    {
        if (e.TryGetProperty("track", out _))
            return null;

        EntityKind kind;
        switch (Text(e, "type"))
        {
            case "album": kind = EntityKind.Album; break;
            case "artist": kind = EntityKind.Artist; break;
            case "playlist": kind = EntityKind.Playlist; break;
            default: return null;
        }

        var id = Text(e, "id");
        return id.Length == 0 ? null : new Entity(kind, id, Text(e, "title"), Text(e, "subtitle"), Text(e, "art"));
    }
}
