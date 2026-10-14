using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Services;
using Player.Core.Playback;

namespace Player.App.ViewModels;

/// <summary>
/// The video of the current track, for the "Reproduciendo" panel and the MV button of the playback bar.
/// Instantiated once from Main.qml as <c>VideoViewModel { id: videoVm }</c>. What the track is gets checked on
/// every track change (it is cheap); the video stream is only fetched while the panel is open and the video is
/// the thing to show. The sound never moves: mpv keeps playing it, and the picture follows its position. Only
/// when the user asks for the video of a studio recording does the sound switch to the video's own audio, so
/// both are the same performance. Method names avoid Set/Reset: the bridge reads those as property setters.
/// </summary>
public class VideoViewModel : INotifyPropertyChanged
{
    private bool _shown;
    private string _trackId = "";
    private VideoChoice _choice = VideoChoice.Unset;
    private VideoPlan _plan = Video.none;

    public VideoViewModel()
    {
        AppServices.EnsureStarted();
        Auto = AppServices.Video.Auto;
        AppServices.Playback.Changed += Poll;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The track has a video (its own, or a counterpart): the MV button shows.</summary>
    public bool Available { get; private set => Set(ref field, value); }
    /// <summary>The video, not the cover, is what to show now.</summary>
    public bool Showing { get; private set => Set(ref field, value); }
    /// <summary>The stream to play while it is showing and the panel is open, "" otherwise.</summary>
    public string Url { get; private set => Set(ref field, value); } = "";
    /// <summary>Waiting for the stream.</summary>
    public bool Loading { get; private set => Set(ref field, value); }
    /// <summary>A short note for the panel (loading, or what went wrong), "" when there is nothing to say.</summary>
    public string Message { get; private set => Set(ref field, value); } = "";
    /// <summary>The setting: official music videos replace the cover by themselves.</summary>
    public bool Auto { get; private set => Set(ref field, value); } = true;

    /// <summary>QML tells the view model whether the panel that shows the video is open.</summary>
    public void PanelShown(bool shown)
    {
        _shown = shown;
        Poll();
    }

    /// <summary>The MV button: video instead of cover, or back, for this track.</summary>
    public void ToggleVideo()
    {
        if (!_plan.Available)
            return;

        _choice = Showing ? VideoChoice.Hide : VideoChoice.Show;
        Poll();
    }

    public void ChooseAuto(bool on)
    {
        AppServices.Video.SaveAuto(on);
        Auto = on;
        Poll();
    }

    /// <summary>The player in QML could not play the stream.</summary>
    public void StreamFailed(string message)
    {
        if (_plan.Available)
            AppServices.Video.ReportFailure(_plan.VideoId, string.IsNullOrWhiteSpace(message) ? "No se pudo reproducir el video." : message);

        Poll();
    }

    public void Retry()
    {
        AppServices.Video.Retry();
        Poll();
    }

    /// <summary>Follows the current track, decides what to show and keeps the sound on the right recording.</summary>
    public void Poll()
    {
        var track = AppServices.Playback.CurrentTrack;
        var id = track?.Id ?? "";

        if (id != _trackId)
        {
            _trackId = id;
            _choice = VideoChoice.Unset;
        }

        AppServices.Video.Select(track);
        var snapshot = AppServices.Video.Snapshot;
        _plan = snapshot.Plan;

        var showing = Video.showing(_plan, Auto, _choice);
        var wantStream = showing && _shown;
        if (wantStream)
            AppServices.Video.EnsureStream();

        Available = _plan.Available;
        Showing = showing;
        Url = wantStream && snapshot.Stream is { } stream ? stream.Url : "";
        Loading = wantStream && snapshot.Stream is null && snapshot.Error.Length == 0;
        Message = !wantStream ? "" : snapshot.Error.Length > 0 ? snapshot.Error : Loading ? "Cargando video…" : "";

        // Studio audio with its video asked for: the sound comes from the video's own recording.
        var audio = Video.audioFrom(_plan, _choice);
        if (audio != (AppServices.Playback.AudioOverride ?? ""))
            AppServices.Playback.SwapAudio(audio.Length > 0 ? audio : null);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
