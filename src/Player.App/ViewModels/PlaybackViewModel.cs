using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Player.App.Services;
using Player.Core.Contracts;
using Player.Core.Playback;

namespace Player.App.ViewModels;

/// <summary>
/// Transport and now-playing for QML. Instantiated from Main.qml as <c>PlaybackViewModel { id: playback }</c>.
/// State lives in <see cref="PlaybackService"/>; this copies it into properties whenever the service says it changed.
/// </summary>
public class PlaybackViewModel : INotifyPropertyChanged
{
    public PlaybackViewModel()
    {
        AppServices.EnsureStarted();
        AppServices.Playback.Changed += Refresh;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Position { get; private set => Set(ref field, value); }
    public double Duration { get; private set => Set(ref field, value); }
    public string PositionText { get; private set => Set(ref field, value); } = "0:00";
    public string DurationText { get; private set => Set(ref field, value); } = "0:00";
    public bool Paused { get; private set => Set(ref field, value); } = true;
    public bool IsLoaded { get; private set => Set(ref field, value); }
    /// <summary>The sound should be playing but is not yet (loading, or the cache is filling). The video holds still.</summary>
    public bool AudioWaiting { get; private set => Set(ref field, value); }
    /// <summary>The same once it has lasted long enough to show a loading bar.</summary>
    public bool Buffering { get; private set => Set(ref field, value); }
    /// <summary>Id of the current track (a YouTube id, or the link or path when it was played directly).</summary>
    public string CurrentId { get; private set => Set(ref field, value); } = "";
    public string CurrentTitle { get; private set => Set(ref field, value); } = "";
    public string CurrentArtist { get; private set => Set(ref field, value); } = "";
    public string CurrentArt { get; private set => Set(ref field, value); } = "";
    /// <summary>"1.2M views · 45K likes" for the current track, "" while unknown.</summary>
    public string StatsText { get; private set => Set(ref field, value); } = "";
    /// <summary>The numbers are being looked up, so the UI timer keeps polling.</summary>
    public bool StatsPending { get; private set => Set(ref field, value); }
    public string RepeatMode { get; private set => Set(ref field, value); } = "off";
    public bool Shuffle { get; private set => Set(ref field, value); }
    /// <summary>0..100, restored from the last session.</summary>
    public int Volume 
    { 
        get => field; 
        set 
        { 
            if (EqualityComparer<int>.Default.Equals(field, value)) return;
            Set(ref field, value);
            AppServices.Playback.SetVolume(value);
        } 
    } = 100;
    public bool CanNext { get; private set => Set(ref field, value); }
    public bool CanPrevious { get; private set => Set(ref field, value); }
    /// <summary>Playback speed, 0.5 to 3.</summary>
    public double Speed { get; private set => Set(ref field, value); } = 1.0;
    /// <summary>"1×", "1.25×".</summary>
    public string SpeedText { get; private set => Set(ref field, value); } = "1×";

    public void Toggle() => AppServices.Playback.Toggle();
    public void Next() => AppServices.Playback.Next();
    public void Previous() => AppServices.Playback.Previous();
    public void CycleRepeat() => AppServices.Playback.CycleRepeat();
    public void CycleSpeed() => AppServices.Playback.CycleSpeed();
    public void ToggleShuffle() => AppServices.Playback.ToggleShuffle();
    public void Seek(double seconds) => AppServices.Playback.Seek(seconds);
    public void SetVolume(int percent) => AppServices.Playback.SetVolume(percent);

    /// <summary>Reads mpv and publishes to MPRIS. QML calls it from a Timer while something is playing.</summary>
    public void Poll() => AppServices.Playback.Poll();

    /// <summary>Runs what the desktop asked for (playerctl, media keys). QML calls it from an always-on Timer.</summary>
    public void Pump() => AppServices.Playback.Pump();

    private void Refresh()
    {
        var p = AppServices.Playback;
        Position = p.Position;
        Duration = p.Duration;
        PositionText = TimeFormat.Clock(p.Position, "0:00");
        DurationText = TimeFormat.Clock(p.Duration, "0:00");
        Paused = p.Paused;
        IsLoaded = p.IsLoaded;
        AudioWaiting = p.AudioWaiting;
        Buffering = p.Buffering;
        CurrentId = p.CurrentId;
        CurrentTitle = p.CurrentTitle;
        CurrentArtist = p.CurrentArtist;
        CurrentArt = p.CurrentArt;
        RepeatMode = p.RepeatMode;
        Shuffle = p.Shuffle;
        Volume = p.Volume;
        CanNext = p.CanNext;
        CanPrevious = p.CanPrevious;
        Speed = p.Speed;
        SpeedText = PlaybackSpeed.label(p.Speed);

        var stats = AppServices.Stats;
        stats.Request(p.CurrentId);
        StatsText = FormatStats(stats.TryGet(p.CurrentId));
        StatsPending = stats.Pending(p.CurrentId);
    }

    private static string FormatStats(TrackStats? stats)
    {
        if (stats is null)
            return "";

        List<string> parts = [];
        if (stats.Views > 0) parts.Add(Tr.F("stats.views", Compact(stats.Views)));
        if (stats.Likes > 0) parts.Add(Tr.F("stats.likes", Compact(stats.Likes)));
        return string.Join(" · ", parts);
    }

    /// <summary>1234 → 1.2K, 1500000 → 1.5M.</summary>
    private static string Compact(long n) => n switch
    {
        >= 1_000_000_000 => Scaled(n, 1e9, "B"),
        >= 1_000_000 => Scaled(n, 1e6, "M"),
        >= 1_000 => Scaled(n, 1e3, "K"),
        _ => n.ToString(CultureInfo.InvariantCulture)
    };

    private static string Scaled(long n, double divisor, string suffix)
    {
        var value = n / divisor;
        return (value >= 100 ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
                             : value.ToString("0.#", CultureInfo.InvariantCulture)) + suffix;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
