using System.Globalization;
using Player.Core;
using Player.Core.Playback;

namespace Player.App.Services;

/// <summary>
/// The small playback settings: keep playing when the queue ends, level the volume between songs and the speed.
/// Read once at start and written through to the settings table, so asking for one never touches the database.
/// </summary>
internal sealed class PlaybackPrefs
{
    private const string AutoPlayKey = "playback.autoplay";
    private const string NormalizeKey = "playback.normalize";
    private const string SpeedKey = "playback.speed";

    private readonly Func<string, string?> _read;
    private readonly Action<string, string> _write;

    public PlaybackPrefs(Func<string, string?> read, Action<string, string> write)
    {
        (_read, _write) = (read, write);
        AutoPlay = Read(AutoPlayKey) != "0";
        Normalize = Read(NormalizeKey) != "0";
        Speed = double.TryParse(Read(SpeedKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
            ? PlaybackSpeed.normalize(speed)
            : 1.0;
    }

    /// <summary>When the queue runs out, go on with similar music. On by default.</summary>
    public bool AutoPlay { get; private set; }

    /// <summary>Turn loud songs down to a common level. On by default (it needs a signed-in account to know the levels).</summary>
    public bool Normalize { get; private set; }

    /// <summary>0.5 to 3, 1 is normal.</summary>
    public double Speed { get; private set; }

    public void SaveAutoPlay(bool on)
    {
        AutoPlay = on;
        Write(AutoPlayKey, on ? "1" : "0");
    }

    public void SaveNormalize(bool on)
    {
        Normalize = on;
        Write(NormalizeKey, on ? "1" : "0");
    }

    public void SaveSpeed(double speed)
    {
        Speed = PlaybackSpeed.normalize(speed);
        Write(SpeedKey, Speed.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private string? Read(string key)
    {
        try { return _read(key); }
        catch (Exception ex)
        {
            Log.Warn("prefs", "read " + key, ex);
            return null;
        }
    }

    private void Write(string key, string value)
    {
        try { _write(key, value); }
        catch (Exception ex) { Log.Warn("prefs", "save " + key, ex); }
    }
}
