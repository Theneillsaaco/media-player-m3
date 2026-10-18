using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Player.App.Models;
using Player.App.Services;
using Player.Core.Localization;
using Player.Core.Lyrics;

namespace Player.App.ViewModels;

/// <summary>
/// The lyrics panel. Instantiated once from Main.qml as <c>LyricsViewModel { id: lyricsVm }</c>. Nothing is
/// looked up while the panel is closed; while it is open, a track change starts the lookup and every playback
/// update moves the highlighted line. Method names avoid the Set/Reset prefixes: the bridge reads those as the
/// setter or reset of the property they name.
/// </summary>
public class LyricsViewModel : INotifyPropertyChanged
{
    private LyricLine[] _lines = [];
    private bool _shown;
    private int _version = -1;
    private string _requested = "";

    public LyricsViewModel()
    {
        AppServices.EnsureStarted();
        FontSize = AppServices.Lyrics.FontSize;
        Message = MessageFor("idle");
        AppServices.Playback.Changed += Poll;
        AppServices.LanguageChanged += () =>
        {
            Message = MessageFor(State);
            ShowOffset(Offset);
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LyricsListModel Lines { get; } = new();

    /// <summary>idle, loading, synced, plain, instrumental, none, error or unavailable.</summary>
    public string State { get; private set => Set(ref field, value); } = "idle";
    /// <summary>What to say when there are no lines to show.</summary>
    public string Message { get; private set => Set(ref field, value); } = "";
    /// <summary>Where the lyrics came from ("YouTube Music", "LRCLIB").</summary>
    public string Source { get; private set => Set(ref field, value); } = "";
    public bool Loading { get; private set => Set(ref field, value); }
    /// <summary>True when the lines have times, so the current one follows the music.</summary>
    public bool Synced { get; private set => Set(ref field, value); }
    /// <summary>The line being sung, -1 before the first one.</summary>
    public int CurrentIndex { get; private set => Set(ref field, value); } = -1;
    /// <summary>Whole seconds until the first line, during a long intro; 0 otherwise.</summary>
    public int Countdown { get; private set => Set(ref field, value); }
    public int FontSize { get; private set => Set(ref field, value); } = LyricsService.DefaultFontSize;
    /// <summary>Seconds later than the music this track's lyrics are shown (negative: sooner).</summary>
    public double Offset { get; private set => Set(ref field, value); }
    public string OffsetText { get; private set => Set(ref field, value); } = "0,0 s";

    /// <summary>QML tells the view model whether the panel is open.</summary>
    public void PanelShown(bool shown)
    {
        _shown = shown;
        if (shown)
            Poll();
    }

    /// <summary>Starts the lookup for a new track, picks up a finished one and moves the current line.</summary>
    public void Poll()
    {
        if (!_shown)
            return;

        var track = AppServices.Playback.CurrentTrack;
        var id = track?.Id ?? "";

        if (id != _requested)
        {
            _requested = id;
            ShowOffset(AppServices.Lyrics.Delay(id));
            AppServices.Lyrics.Request(track);
        }

        var snapshot = AppServices.Lyrics.Snapshot;
        if (snapshot.Version != _version)
            Apply(snapshot);

        Follow();
    }

    /// <summary>Jumps the music to the start of a line (timed lyrics only).</summary>
    public void SeekToLine(int index)
    {
        if (!Synced || index < 0 || index >= _lines.Length)
            return;

        AppServices.Playback.Seek(Math.Max(0, _lines[index].Start + Offset));
    }

    public void NudgeOffset(double seconds) => ChangeOffset(LyricsSync.nudge(Offset, seconds));

    public void ClearOffset() => ChangeOffset(0);

    public void GrowText() => ChangeFontSize(2);

    public void ShrinkText() => ChangeFontSize(-2);

    /// <summary>Looks again after an error.</summary>
    public void Retry()
    {
        if (AppServices.Playback.CurrentTrack is { } track)
            AppServices.Lyrics.Request(track, force: true);
    }

    private void Apply(LyricsSnapshot snapshot)
    {
        _version = snapshot.Version;

        _lines = snapshot.Stage switch
        {
            LyricsStage.Synced => Lrc.parse(snapshot.Text),
            LyricsStage.Plain => Lrc.plain(snapshot.Text),
            _ => [],
        };

        var stage = snapshot.Stage;
        if (_lines.Length == 0 && stage is LyricsStage.Synced or LyricsStage.Plain)
            stage = LyricsStage.None; // a stored text with nothing usable in it

        var name = stage.ToString().ToLowerInvariant();
        State = name;
        Message = MessageFor(name);
        Source = _lines.Length > 0 ? snapshot.Source : "";
        Loading = stage == LyricsStage.Loading;
        Synced = stage == LyricsStage.Synced;
        CurrentIndex = -1;
        Countdown = 0;
        Lines.Replace(_lines.Select(l => new LyricRow { Text = l.Text, Start = l.Start }));
    }

    private void Follow()
    {
        if (!Synced)
            return;

        var position = LyricsSync.delayed(AppServices.Playback.Position, Offset);
        CurrentIndex = LyricsSync.currentIndex(_lines, position);
        Countdown = LyricsSync.introCountdown(_lines, position) is { IsSome: true } left ? (int)Math.Ceiling(left.Value) : 0;
    }

    private void ChangeOffset(double seconds)
    {
        if (_requested.Length == 0)
            return;

        ShowOffset(seconds);
        AppServices.Lyrics.SaveDelay(_requested, seconds);
        Follow();
    }

    private void ShowOffset(double seconds)
    {
        Offset = seconds;
        OffsetText = seconds.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture).Replace('.', I18n.decimalSeparator()) + " s";
    }

    private void ChangeFontSize(int step)
    {
        FontSize = Math.Clamp(FontSize + step, LyricsService.MinFontSize, LyricsService.MaxFontSize);
        AppServices.Lyrics.SaveFontSize(FontSize);
    }

    private static string MessageFor(string state) => state switch
    {
        "loading" => Tr.T("lyrics.loading"),
        "none" => Tr.T("lyrics.none"),
        "instrumental" => Tr.T("lyrics.instrumental"),
        "error" => Tr.T("lyrics.error"),
        "unavailable" => Tr.T("lyrics.unavailable"),
        _ => Tr.T("lyrics.idle"),
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
