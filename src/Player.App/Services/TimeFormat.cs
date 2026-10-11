using System.Globalization;

namespace Player.App.Services;

internal static class TimeFormat
{
    /// <summary>m:ss. Returns <paramref name="whenZero"/> for zero or less (unknown durations show nothing in lists).</summary>
    public static string Clock(double seconds, string whenZero = "")
    {
        if (seconds <= 0)
            return whenZero;

        var s = (int)seconds;
        
        return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}