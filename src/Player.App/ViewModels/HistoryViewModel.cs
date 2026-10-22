using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Player.App.Models;
using Player.App.Services;
using Player.Core;
using Player.Core.Domain;
using Player.Storage.Database;

namespace Player.App.ViewModels;

/// <summary>
/// The history page: the songs that counted as a listen, newest first. Instantiated once from Main.qml as
/// <c>HistoryViewModel { id: historyVm }</c>; Main.qml calls <see cref="Load"/> when the page is opened.
/// </summary>
public class HistoryViewModel : INotifyPropertyChanged
{
    private const int Limit = 300;

    private IReadOnlyList<Track> _tracks = [];

    public HistoryViewModel()
    {
        AppServices.EnsureStarted();
        AppServices.LanguageChanged += Load; // the dates are written in the language of the interface
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackListModel Items { get; } = new();
    public int Count { get; private set => Set(ref field, value); }

    public void Load()
    {
        IReadOnlyList<HistoryEntry> entries;

        try { entries = AppServices.Db?.GetHistory(Limit) ?? []; }
        catch (Exception e)
        {
            Log.Warn("history", "read", e);
            entries = [];
        }

        _tracks = [.. entries.Select(ToTrack)];
        Items.Replace(entries.Select(ToRow));
        Count = entries.Count;
    }

    public void ClearAll()
    {
        try { AppServices.Db?.ClearHistory(); }
        catch (Exception e) { Log.Warn("history", "clear", e); }

        Load();
    }

    public void PlayItem(int index) => AppServices.Playback.PlayFrom(_tracks, index);

    public void AddItem(int index) => AppServices.Playback.AddFrom(_tracks, index);

    public void PlayNextItem(int index) => AppServices.Playback.PlayNextFrom(_tracks, index);

    private static string ArtOf(string id) => id.Length == 11 ? "https://i.ytimg.com/vi/" + id + "/mqdefault.jpg" : "";

    private static Track ToTrack(HistoryEntry e)
    {
        string[] artists = e.Artist.Length > 0 ? [e.Artist] : [];
        return new Track(e.TrackId, e.Title, e.Artist, artists, e.Album, e.Duration, ArtOf(e.TrackId), TrackKind.Song);
    }

    private static TrackRow ToRow(HistoryEntry e)
    {
        var when = DateTimeOffset.FromUnixTimeSeconds(e.StartedAt).ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture);

        return new TrackRow
        {
            VideoId = e.TrackId,
            Title = e.Title,
            Artist = e.Artist.Length > 0 ? e.Artist + " · " + when : when,
            Album = e.Album,
            Duration = TimeFormat.Clock(e.Duration),
            ArtUrl = ArtOf(e.TrackId)
        };
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
