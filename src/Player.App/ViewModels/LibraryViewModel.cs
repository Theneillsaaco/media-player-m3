using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Models;
using Player.App.Services;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.App.ViewModels;

/// <summary>
/// The user's YouTube Music library: playlists, the open playlist's tracks, and the heart (likes) of the
/// playing track. Background work leaves results in a queue that <see cref="Poll"/> applies on the Qt thread.
/// </summary>
public class LibraryViewModel : INotifyPropertyChanged
{
    private const string LikedList = "LM"; // YouTube Music's automatic Liked Music playlist

    private readonly BackgroundWork _work = new();
    private IReadOnlyList<PlaylistInfo> _playlists = [];
    private IReadOnlyList<Track> _listTracks = [];
    private HashSet<string> _likedIds = [];
    private bool _libraryLoaded;

    public LibraryViewModel()
    {
        AppServices.EnsureStarted();
        _likedIds = [.. (SafeCache(() => AppServices.Db?.LoadTracks(LikedList)) ?? []).Select(t => t.Id)];
        AppServices.Playback.Changed += UpdateLikeState;
        AppServices.AccountChanged += SyncAccount;
        SyncAccount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The user's playlists (title, "N canciones", cover).</summary>
    public TrackListModel Library { get; } = new();
    /// <summary>The tracks of the playlist that is open.</summary>
    public TrackListModel Tracks { get; } = new();

    public string LibraryStatus { get; private set => Set(ref field, value); } = "";
    /// <summary>Id of the open playlist, "" while the playlist list is showing.</summary>
    public string OpenListId { get; private set => Set(ref field, value); } = "";
    public string OpenListTitle { get; private set => Set(ref field, value); } = "";
    public string ListStatus { get; private set => Set(ref field, value); } = "";
    public int ListCount { get; private set => Set(ref field, value); }
    public bool CanLike { get; private set => Set(ref field, value); }
    public bool Liked { get; private set => Set(ref field, value); }

    // The library page shows these when no account is connected. State is one of: unavailable, signedOut, signedIn, expired.
    public string AccountState { get; private set => Set(ref field, value); } = "unavailable";
    public string AccountStatus { get; private set => Set(ref field, value); } = "";
    public bool AccountBusy { get; private set => Set(ref field, value); }

    /// <summary>Anything in the background that the UI timer must keep polling for.</summary>
    public bool Working { get; private set => Set(ref field, value); }

    /// <summary>Applies finished background work and follows the account state. QML calls it from a Timer.</summary>
    public void Poll()
    {
        _work.Drain();
        SyncAccount();
        UpdateLikeState();
        Working = _work.Busy || AccountBusy;
    }

    /// <summary>Loads the playlist list the first time the library is shown.</summary>
    public void LoadLibrary()
    {
        if (AppServices.Account is not { } account || AccountState != "signedIn" || _libraryLoaded) return;
        LibraryStatus = "Cargando biblioteca…";
        Run(Task.Run(() => account.GetLibraryAsync()),
            lists =>
            {
                _libraryLoaded = true;
                _playlists = lists;
                Library.Replace(lists.Select(Rows.FromPlaylist));
                LibraryStatus = lists.Count == 0 ? "No hay listas en tu biblioteca" : "";
            },
            e => LibraryStatus = ErrorText(e));
    }

    /// <summary>Opens a playlist: the cached tracks show at once, fresh ones replace them when they arrive.</summary>
    public void OpenPlaylist(int index)
    {
        if (AppServices.Account is not { } account || index < 0 || index >= _playlists.Count) return;
        var list = _playlists[index];
        var id = list.Id;
        OpenListId = id;
        OpenListTitle = list.Title;

        var cached = SafeCache(() => AppServices.Db?.LoadTracks(id)) ?? [];
        SetListTracks(cached);
        ListStatus = cached.Count == 0 ? "Cargando…" : "Actualizando…";

        Run(Task.Run(async () =>
            {
                var tracks = await account.GetPlaylistAsync(id);
                SafeCache(() => { AppServices.Db?.SaveTracks(id, tracks); return 0; });
                return tracks;
            }),
            tracks =>
            {
                if (id == LikedList) ReplaceLiked(tracks);
                if (OpenListId != id) return; // the user moved on
                SetListTracks(tracks);
                ListStatus = tracks.Count == 0 ? "Esta lista está vacía" : "";
            },
            e =>
            {
                if (OpenListId != id) return;
                ListStatus = cached.Count == 0 ? ErrorText(e) : "No se pudo actualizar: " + e.Message;
            });
    }

    public void CloseList()
    {
        OpenListId = "";
        OpenListTitle = "";
        ListStatus = "";
        SetListTracks([]);
    }

    /// <summary>Replaces the queue with the open playlist, in order or shuffled.</summary>
    public void PlayList(bool shuffle) => AppServices.Playback.PlayList(_listTracks, shuffle);

    public void PlayListItem(int index) => AppServices.Playback.PlayFrom(_listTracks, index);

    public void AddListItem(int index) => AppServices.Playback.AddFrom(_listTracks, index);

    /// <summary>Likes or unlikes the playing track on YouTube Music.</summary>
    public void ToggleLike()
    {
        if (AppServices.Account is not { } account || !CanLike) return;
        var id = AppServices.Playback.CurrentId;
        var like = !_likedIds.Contains(id);
        SetLiked(id, like);

        Run(Task.Run(() => account.RateAsync(id, like)),
            _ => { },
            e =>
            {
                SetLiked(id, !like); // it did not go through: put the heart back
                AppServices.Status.Text = "No se pudo cambiar el Me gusta: " + e.Message;
            });
    }

    private void SetLiked(string id, bool liked)
    {
        if (liked) _likedIds.Add(id); else _likedIds.Remove(id);
        UpdateLikeState();
    }

    /// <summary>Fetches Liked Music in the background so the heart knows every liked song.</summary>
    private void RefreshLiked()
    {
        if (AppServices.Account is not { } account) return;
        Run(Task.Run(async () =>
            {
                var tracks = await account.GetPlaylistAsync(LikedList);
                SafeCache(() => { AppServices.Db?.SaveTracks(LikedList, tracks); return 0; });
                return tracks;
            }),
            ReplaceLiked,
            e => Log.Warn("library", "liked songs", e));
    }

    private void ReplaceLiked(IReadOnlyList<Track> tracks)
    {
        _likedIds = [.. tracks.Select(t => t.Id)];
        UpdateLikeState();
    }

    private void UpdateLikeState()
    {
        var p = AppServices.Playback;
        CanLike = AccountState == "signedIn" && p.IsLoaded && p.CurrentId.Length > 0 && !TrackSource.IsDirect(p.CurrentId);
        Liked = CanLike && _likedIds.Contains(p.CurrentId);
    }

    private void SetListTracks(IReadOnlyList<Track> tracks)
    {
        _listTracks = tracks;
        Tracks.Replace(tracks.Select(Rows.FromTrack));
        ListCount = tracks.Count;
    }

    private void SyncAccount()
    {
        if (AppServices.Account is not { } account) return;
        var s = account.Snapshot;
        var state = AccountView.StateName(s.Stage);
        var wasSignedIn = AccountState == "signedIn";
        AccountState = state;
        AccountStatus = s.Status;
        AccountBusy = s.Busy;

        if (state == "signedIn" && !wasSignedIn)
        {
            _libraryLoaded = false;
            RefreshLiked();
        }
        else if (state != "signedIn" && wasSignedIn)
        {
            _libraryLoaded = false;
            _playlists = [];
            _likedIds = [];
            Library.Replace([]);
            CloseList();
            LibraryStatus = "";
        }
        Working = _work.Busy || AccountBusy;
    }

    private void Run<T>(Task<T> task, Action<T> done, Action<Exception> failed)
    {
        _work.Run(task, done, failed);
        Working = true;
    }

    private static string ErrorText(Exception e) =>
        e is AuthExpiredException ? "La sesión caducó: vuelve a importarla en Ajustes" : "Error: " + e.Message;

    private static T? SafeCache<T>(Func<T?> read)
    {
        try { return read(); }
        catch (Exception e)
        {
            Log.Warn("library", "cache", e);
            return default;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
