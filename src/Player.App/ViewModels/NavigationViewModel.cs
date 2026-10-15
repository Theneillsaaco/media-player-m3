using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Models;
using Player.App.Services;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.ViewModels;

/// <summary>
/// Where the user is: one of the three root sections (home, search, library) and, on top of it, a stack of
/// detail pages (album, playlist or mix, artist) with back. Instantiated once from Main.qml as
/// <c>NavigationViewModel { id: navVm }</c>. Pages load on the thread pool and are kept in memory for a few
/// minutes, so going back is instant; <see cref="Poll"/> applies what arrived, on the Qt thread.
/// Method names avoid Set/Reset: the bridge reads those as property setters.
/// </summary>
public class NavigationViewModel : INotifyPropertyChanged
{
    private readonly record struct Entry(string Page, string Id);

    private const long Ttl = 300;      // seconds a detail page stays fresh
    private const long HomeTtl = 120;
    private const int MaxCached = 40;

    private readonly BackgroundWork _work = new();
    private readonly List<Entry> _stack = [new("search", "")];
    private readonly Dictionary<string, (long At, object Value)> _cache = [];
    private readonly Dictionary<string, TrackLinks> _links = [];
    private readonly Dictionary<int, (IReadOnlyList<Track> List, int Index)> _homeSlots = [];
    private IReadOnlyList<Track> _collection = [];
    private IReadOnlyList<Track> _artistSongs = [];
    private string _collectionArtistId = "";

    public NavigationViewModel()
    {
        AppServices.EnsureStarted();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The root section ("home", "search" or "library") the current page belongs to.</summary>
    public string Section { get; private set => Set(ref field, value); } = "search";
    /// <summary>What is on screen: home, search, library, album, collection (playlist or mix) or artist.</summary>
    public string Page { get; private set => Set(ref field, value); } = "search";
    public bool CanBack { get; private set => Set(ref field, value); }
    public string Title { get; private set => Set(ref field, value); } = "";
    /// <summary>Loading note or error of the page, "" when there is nothing to say.</summary>
    public string Status { get; private set => Set(ref field, value); } = "";
    public bool Failed { get; private set => Set(ref field, value); }
    /// <summary>Something is loading in the background, so the UI timer keeps polling.</summary>
    public bool Working { get; private set => Set(ref field, value); }
    /// <summary>A short message for the snackbar ("" when there is none).</summary>
    public string Message { get; private set => Set(ref field, value); } = "";

    // Home: shelf titles and their entries in one flat list (Kind "header", "track", "album"...).
    public EntityListModel HomeRows { get; } = new();

    // Album, playlist or mix.
    public TrackListModel Collection { get; } = new();
    public string CollectionTitle { get; private set => Set(ref field, value); } = "";
    public string CollectionSubtitle { get; private set => Set(ref field, value); } = "";
    public string CollectionArt { get; private set => Set(ref field, value); } = "";
    public int CollectionCount { get; private set => Set(ref field, value); }
    public bool CanOpenCollectionArtist { get; private set => Set(ref field, value); }

    // Artist.
    public string ArtistName { get; private set => Set(ref field, value); } = "";
    public string ArtistDescription { get; private set => Set(ref field, value); } = "";
    public string ArtistArt { get; private set => Set(ref field, value); } = "";
    public TrackListModel ArtistSongs { get; } = new();
    public EntityListModel ArtistAlbums { get; } = new();
    public EntityListModel ArtistSingles { get; } = new();
    public EntityListModel ArtistRelated { get; } = new();

    // ---- going places ----

    public void GoRoot(string section)
    {
        if (section is not ("home" or "search" or "library"))
            return;

        _stack.Clear();
        _stack.Add(new Entry(section, ""));
        Show();
    }

    public void GoBack()
    {
        if (_stack.Count <= 1)
            return;

        _stack.RemoveAt(_stack.Count - 1);
        Show();
    }

    /// <summary>Opens an album, artist or playlist (kind as the entity rows name it).</summary>
    public void OpenEntity(string kind, string id)
    {
        var page = kind switch { "album" => "album", "artist" => "artist", "playlist" => "collection", _ => "" };
        if (page.Length == 0 || id.Length == 0)
            return;

        if (_stack[^1] == new Entry(page, id))
            return;

        _stack.Add(new Entry(page, id));
        Show();
    }

    public void OpenTrackArtist(string videoId) => ResolveLink(videoId, artist: true);

    public void OpenTrackAlbum(string videoId) => ResolveLink(videoId, artist: false);

    public void OpenCollectionArtist() => OpenEntity("artist", _collectionArtistId);

    /// <summary>Asks again for the page on screen (after an error).</summary>
    public void Retry()
    {
        var entry = _stack[^1];
        _cache.Remove(entry.Page + ":" + entry.Id);
        Show();
    }

    public void DismissMessage() => Message = "";

    // ---- playing from the pages ----

    public void PlayHomeRow(int row)
    {
        if (_homeSlots.TryGetValue(row, out var slot))
            AppServices.Playback.PlayFrom(slot.List, slot.Index);
    }

    public void PlayCollection(bool shuffle) => AppServices.Playback.PlayList(_collection, shuffle);

    public void PlayCollectionItem(int index) => AppServices.Playback.PlayFrom(_collection, index);

    public void AddCollectionItem(int index) => AppServices.Playback.AddFrom(_collection, index);

    public void PlayArtist(bool shuffle) => AppServices.Playback.PlayList(_artistSongs, shuffle);

    public void PlayArtistSong(int index) => AppServices.Playback.PlayFrom(_artistSongs, index);

    public void AddArtistSong(int index) => AppServices.Playback.AddFrom(_artistSongs, index);

    /// <summary>Applies finished background work. QML calls it from a Timer while something loads.</summary>
    public void Poll()
    {
        _work.Drain();
        Working = _work.Busy;
    }

    // ---- loading ----

    private void Show()
    {
        var entry = _stack[^1];
        Section = _stack[0].Page;
        Page = entry.Page;
        CanBack = _stack.Count > 1;
        Title = "";
        Status = "";
        Failed = false;

        if (entry.Page is "home" or "album" or "collection" or "artist")
            Load(entry);
    }

    private void Load(Entry entry)
    {
        var key = entry.Page + ":" + entry.Id;

        if (TryCached(key, entry.Page == "home" ? HomeTtl : Ttl, out var cached))
        {
            Apply(cached);
            return;
        }

        if (AppServices.Provider is not { } provider)
        {
            Status = "La búsqueda no está disponible.";
            Failed = true;
            return;
        }

        if (entry.Page is "album" or "collection")
            ApplyCollection(new TrackCollection("", "", "", "", "", []));
        else if (entry.Page == "artist")
            ApplyArtist(new ArtistPage("", "", "", "", "", [], [], [], []));

        Status = "Cargando…";

        switch (entry.Page)
        {
            case "home": Fetch(entry, key, () => provider.Home()); break;
            case "album": Fetch(entry, key, () => provider.Album(entry.Id)); break;
            case "collection": Fetch(entry, key, () => provider.Collection(entry.Id)); break;
            case "artist": Fetch(entry, key, () => provider.Artist(entry.Id)); break;
        }
    }

    private void Fetch<T>(Entry entry, string key, Func<Task<T>> call) where T : class
    {
        _work.Run(Task.Run(call),
            value =>
            {
                if (_cache.Count >= MaxCached)
                    _cache.Clear();

                _cache[key] = (Now(), value);

                if (_stack[^1] == entry)
                    Apply(value);
            },
            error =>
            {
                if (_stack[^1] != entry)
                    return;

                Status = error is AuthExpiredException
                    ? "La sesión caducó: vuelve a importarla en Ajustes"
                    : "Error: " + error.Message;
                Failed = true;
            });

        Working = true;
    }

    private void Apply(object value)
    {
        Status = "";
        Failed = false;

        switch (value)
        {
            case Shelf[] shelves: ApplyHome(shelves); break;
            case TrackCollection collection: ApplyCollection(collection); break;
            case ArtistPage artist: ApplyArtist(artist); break;
        }
    }

    private void ApplyHome(Shelf[] shelves)
    {
        List<EntityRow> rows = [];
        _homeSlots.Clear();

        foreach (var shelf in shelves)
        {
            if (shelf.Tracks.Length + shelf.Entities.Length == 0)
                continue;

            rows.Add(Rows.Header(shelf.Title));

            for (var i = 0; i < shelf.Tracks.Length; i++)
            {
                _homeSlots[rows.Count] = (shelf.Tracks, i);
                rows.Add(Rows.FromTrackEntity(shelf.Tracks[i]));
            }

            rows.AddRange(shelf.Entities.Select(Rows.FromEntity));
        }

        HomeRows.Replace(rows);
        Title = "Inicio";
        Status = rows.Count == 0 ? "No hay recomendaciones por ahora." : "";
    }

    private void ApplyCollection(TrackCollection collection)
    {
        _collection = collection.Tracks;
        _collectionArtistId = collection.ArtistId;
        Collection.Replace(collection.Tracks.Select(Rows.FromTrack));
        CollectionTitle = collection.Title;
        CollectionSubtitle = collection.Subtitle;
        CollectionArt = collection.ArtUrl;
        CollectionCount = collection.Tracks.Length;
        CanOpenCollectionArtist = collection.ArtistId.Length > 0;
        Title = collection.Title;

        if (collection.Title.Length > 0 && collection.Tracks.Length == 0)
            Status = "Esta lista está vacía.";
    }

    private void ApplyArtist(ArtistPage artist)
    {
        _artistSongs = artist.Songs;
        ArtistName = artist.Name;
        ArtistDescription = artist.Description;
        ArtistArt = artist.ArtUrl;
        ArtistSongs.Replace(artist.Songs.Take(8).Select(Rows.FromTrack));
        ArtistAlbums.Replace(artist.Albums.Select(Rows.FromEntity));
        ArtistSingles.Replace(artist.Singles.Select(Rows.FromEntity));
        ArtistRelated.Replace(artist.Related.Select(Rows.FromEntity));
        Title = artist.Name;
    }

    /// <summary>A name in a list opens its page: first the ids behind the track are looked up.</summary>
    private void ResolveLink(string videoId, bool artist)
    {
        if (videoId.Length == 0 || TrackSource.IsDirect(videoId) || AppServices.Provider is not { } provider)
            return;

        if (_links.TryGetValue(videoId, out var known))
        {
            OpenLink(known, artist);
            return;
        }

        _work.Run(Task.Run(() => provider.Links(videoId)),
            links =>
            {
                _links[videoId] = links;
                OpenLink(links, artist);
            },
            error => Message = "No se pudo abrir la página: " + error.Message);

        Working = true;
    }

    private void OpenLink(TrackLinks links, bool artist)
    {
        var id = artist ? links.ArtistId : links.AlbumId;

        if (id.Length == 0)
            Message = artist ? "Esta canción no tiene página de artista." : "Esta canción no tiene álbum.";
        else
            OpenEntity(artist ? "artist" : "album", id);
    }

    private bool TryCached(string key, long ttl, out object value)
    {
        if (_cache.TryGetValue(key, out var hit) && Now() - hit.At < ttl)
        {
            value = hit.Value;
            return true;
        }

        value = null!;
        return false;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
