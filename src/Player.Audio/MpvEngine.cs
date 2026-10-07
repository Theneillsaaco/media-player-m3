using System.Globalization;
using System.Runtime.InteropServices;
using Player.Core;

namespace Player.Audio;

/// <summary>libmpv through P/Invoke. Audio only, no window, no config files.</summary>
public sealed class MpvEngine : IAudioEngine
{
    private const string Lib = "mpv";

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent { public int Id; public int Error; public ulong ReplyUserdata; public IntPtr Data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEndFile { public int Reason; public int Error; }

    [DllImport(Lib)] private static extern IntPtr mpv_create();
    [DllImport(Lib)] private static extern int mpv_initialize(IntPtr h);
    [DllImport(Lib)] private static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport(Lib)] private static extern int mpv_set_option_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(Lib)] private static extern int mpv_set_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(Lib)] private static extern IntPtr mpv_get_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] private static extern int mpv_command(IntPtr h, IntPtr[] args);
    [DllImport(Lib)] private static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport(Lib)] private static extern IntPtr mpv_error_string(int error);
    [DllImport("libc", EntryPoint = "setlocale")] private static extern IntPtr setlocale(int category, string locale);

    static MpvEngine()
    {
        NativeLibrary.SetDllImportResolver(typeof(MpvEngine).Assembly, (name, asm, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            foreach (var candidate in new[] { "libmpv.so.2", "libmpv.so.1", "libmpv.so" })
                if (NativeLibrary.TryLoad(candidate, asm, path, out var handle)) return handle;
            return IntPtr.Zero;
        });
    }

    private readonly IntPtr _h;
    private bool _disposed;

    public MpvEngine()
    {
        setlocale(1, "C"); // LC_NUMERIC must be "C" or libmpv refuses to start
        _h = mpv_create();
        if (_h == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");

        var options = new (string, string)[]
        {
            ("config", "no"), ("terminal", "no"), ("idle", "yes"), ("vid", "no"),
            ("audio-display", "no"), ("input-default-bindings", "no"), ("input-vo-keyboard", "no"),
            ("ytdl", "yes"), ("ytdl-format", "bestaudio/best"), ("audio-client-name", "Player"),
        };
        foreach (var (key, value) in options)
        {
            var rc = mpv_set_option_string(_h, key, value);
            if (rc < 0) Console.Error.WriteLine($"mpv option {key}={value}: {Describe(rc)}");
        }

        var init = mpv_initialize(_h);
        if (init < 0) throw new InvalidOperationException("mpv_initialize: " + Describe(init));
    }

    public double Position => GetDouble("time-pos");
    public double Duration => GetDouble("duration");
    public bool Paused => Get("pause") == "yes";
    public bool Idle => Get("idle-active") == "yes";

    public void Load(string source)
    {
        SetPaused(false);
        Command("loadfile", source, "replace");
    }

    public void SetPaused(bool paused) => mpv_set_property_string(_h, "pause", paused ? "yes" : "no");

    public void Seek(double seconds) =>
        Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");

    public void SetVolume(int percent) =>
        mpv_set_property_string(_h, "volume", Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture));

    public string DrainEvents()
    {
        string? error = null;
        for (;;)
        {
            var ev = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_h, 0));
            if (ev.Id == 0) return error ?? ""; // MPV_EVENT_NONE: queue empty
            if (ev.Id == 7 && ev.Data != IntPtr.Zero) // MPV_EVENT_END_FILE
            {
                var end = Marshal.PtrToStructure<MpvEndFile>(ev.Data);
                if (end.Reason == 4) error = Describe(end.Error); // MPV_END_FILE_REASON_ERROR
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
        if (p == IntPtr.Zero) return null;
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
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            return mpv_command(_h, ptrs);
        }
        finally
        {
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    private static string Describe(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? $"error {code}";
}
