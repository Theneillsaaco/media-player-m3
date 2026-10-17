using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Player.Core;
using Player.Core.Contracts;
using static Player.Audio.Mpv.Native;

namespace Player.Audio.Mpv;

/// <summary>
/// libmpv through P/Invoke (see <see cref="Native"/>). Audio only, no window, no config files.
/// The playlist never holds more than two entries: the current track and, when one is queued, the next.
/// </summary>
public class MpvEngine : IAudioEngine
{
    public void Load(string source, string[] headers)
    {
        SetPaused(true);
        Command(LoadArgs("replace", source, headers));
    }

    public void QueueNext(string source, string[] headers)
    {
        Command("playlist-clear"); // keeps only the entry that is playing
        Command(LoadArgs("append", source, headers));
    }

    public void ClearNext() => Command("playlist-clear");
    
    public void SetPaused(bool paused) =>
        mpv_set_property_string(_h, "pause", paused ? "yes" : "no");

    public void Seek(double seconds) =>
        Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");

    public void SetVolume(int percent) => 
        mpv_set_property_string(_h, "volume", Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture));

    public bool TakeEnded()
    {
        var ended = _ended;
        _ended = false;
        return ended;
    }
    
    public string DrainEvents()
    {
        string? error = null;
        for (;;)
        {
            var ev = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_h, 0));
            
            if (ev.Id == 0)
                return error ?? ""; // MPV_EVENT_NONE: queue empty
            if (ev.Id == 7 && ev.Data != IntPtr.Zero) // MPV_EVENT_END_FILE
            {
                var end = Marshal.PtrToStructure<MpvEndFile>(ev.Data);
                if (end.Reason == 0)
                    _ended = true; // MPV_END_FILE_REASON_EOF: the track played to its end (mpv may have gone on into the queued one)
                else if (end.Reason == 4)
                    error = Describe(end.Error); // MPV_END_FILE_REASON_ERROR
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        mpv_terminate_destroy(_h);
    }
    
    private string? Get(string name)
    {
        var p = mpv_get_property_string(_h, name);
        
        if (p == IntPtr.Zero) 
            return null;
        
        try { return Marshal.PtrToStringUTF8(p); }
        finally { mpv_free(p); }
    }
    
    private double GetDouble(string name) =>
        double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    
    private int Command(params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1]; // null-terminated
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);

            return mpv_command(_h, ptrs);
        }
        finally
        {
            foreach (var p in ptrs)
                if (p != IntPtr.Zero) 
                    Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>loadfile with per-file options only when there are headers (the index argument needs mpv 0.38+).</summary>
    private static string[] LoadArgs(string flag, string source, string[] headers)
    {
        var options = FileOptions(headers);
        return options.Length == 0 ? ["loadfile", source, flag] : ["loadfile", source, flag, "-1", options];
    }
    
    /// <summary>
    /// user-agent and http-header-fields for one file. mpv quotes a value as %length%value; the list is
    /// quoted per header and then once more as a whole, so commas inside a header survive both levels.
    /// </summary>
    private static string FileOptions(string[] headers)
    {
        List<string> options = [];
        List<string> fields = [];

        foreach (var header in headers)
        {
            var colon = header.IndexOf(':');
            if (colon < 0)
                continue;
            
            if (header[..colon].Trim().Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                options.Add("user-agent=" + Quote(header[(colon + 1)..].Trim()));
            else
                fields.Add(Quote(header));
        }
        
        if (fields.Count > 0)
            options.Add("http-header-fields=" + Quote(string.Join(",", fields)));
        
        return string.Join(",", options);
    }
    
    private static string Quote(string value) =>
        "%" + Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + "%" + value;
    
    private static string Describe(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? $"error {code}";
    
    #region Fields

    private readonly IntPtr _h;
    private bool _disposed;
    private bool _ended;
    
    public double Position => GetDouble("time-pos");
    public double Duration => GetDouble("duration");
    public bool Paused => Get("pause") == "yes";
    public bool Idle => Get("idle-active") == "yes";
    
    public MpvEngine()
    {
        setlocale(1, "C"); // LC_NUMERIC must be "C" or libmpv refuses to start
        _h = mpv_create();
        
        if (_h == IntPtr.Zero) 
            throw new InvalidOperationException("mpv_create failed");

        foreach (var (key, value) in MpvOptions.Defaults)
        {
            var rc = mpv_set_option_string(_h, key, value);
            if (rc < 0) 
                Log.Warn("mpv", $"option {key}={value}: {Describe(rc)}");
        }
        
        var init = mpv_initialize(_h);
        if (init < 0) 
            throw new InvalidOperationException("mpv_initialize: " + Describe(init));
    }

    #endregion
}
