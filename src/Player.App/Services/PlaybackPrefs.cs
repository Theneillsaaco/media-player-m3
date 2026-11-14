using System.Globalization;
using Player.Core.Playback;

namespace Player.App.Services;

/// <summary>
/// The small playback settings: keep playing when the queue ends, level the volume between songs, the speed and
/// how many songs the disk cache keeps. Read once at start and written through to the settings table, so asking
/// for one never touches the database.
/// </summary>
internal sealed class PlaybackPrefs
{
    private const string AutoPlayKey = "playback.autoplay";
    private const string NormalizeKey = "playback.normalize";
    private const string SpeedKey = "playback.speed";
    private const string CacheKey = "cache.limit";

    private readonly SafeSettings _settings;

    public PlaybackPrefs(SafeSettings settings)
    {
        _settings = settings;
        AutoPlay = settings.Get(AutoPlayKey) != "0";
        Normalize = settings.Get(NormalizeKey) != "0";
        Speed = double.TryParse(settings.Get(SpeedKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
            ? PlaybackSpeed.normalize(speed)
            : 1.0;

        CacheLimit = int.TryParse(settings.Get(CacheKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
            ? CachePolicy.normalizeLimit(limit)
            : CachePolicy.defaultLimit;
    }

    /// <summary>When the queue runs out, go on with similar music. On by default.</summary>
    public bool AutoPlay { get; private set; }

    /// <summary>Turn loud songs down to a common level. On by default (it needs a signed-in account to know the levels).</summary>
    public bool Normalize { get; private set; }

    /// <summary>0.5 to 3, 1 is normal.</summary>
    public double Speed { get; private set; }

    /// <summary>How many songs the disk cache keeps; 0 turns it off.</summary>
    public int CacheLimit { get; private set; }

    public void SaveCacheLimit(int limit)
    {
        CacheLimit = CachePolicy.normalizeLimit(limit);
        _settings.Set(CacheKey, CacheLimit.ToString(CultureInfo.InvariantCulture));
    }

    public void SaveAutoPlay(bool on)
    {
        AutoPlay = on;
        _settings.Set(AutoPlayKey, on ? "1" : "0");
    }

    public void SaveNormalize(bool on)
    {
        Normalize = on;
        _settings.Set(NormalizeKey, on ? "1" : "0");
    }

    public void SaveSpeed(double speed)
    {
        Speed = PlaybackSpeed.normalize(speed);
        _settings.Set(SpeedKey, Speed.ToString("0.##", CultureInfo.InvariantCulture));
    }
}
