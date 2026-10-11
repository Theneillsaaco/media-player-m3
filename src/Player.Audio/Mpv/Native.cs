using System.Runtime.InteropServices;

namespace Player.Audio.Mpv;

/// <summary>libmpv P/Invoke declarations and the resolver that finds the shared library. Nothing else calls native code.</summary>
internal static class Native
{
    private const string Lib = "mpv";

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent { public int Id; public int Error; public ulong ReplyUserdata; public IntPtr Data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEndFile { public int Reason; public int Error; }

    [DllImport(Lib)] public static extern IntPtr mpv_create();
    [DllImport(Lib)] public static extern int mpv_initialize(IntPtr h);
    [DllImport(Lib)] public static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport(Lib)] public static extern int mpv_set_option_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(Lib)] public static extern int mpv_set_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(Lib)] public static extern IntPtr mpv_get_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] public static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] public static extern int mpv_command(IntPtr h, IntPtr[] args);
    [DllImport(Lib)] public static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport(Lib)] public static extern IntPtr mpv_error_string(int error);
    [DllImport("libc", EntryPoint = "setlocale")] public static extern IntPtr setlocale(int category, string locale);

    // Runs before the first native call (the type has an explicit static constructor).
    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, asm, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            foreach (var candidate in new[] { "libmpv.so.2", "libmpv.so.1", "libmpv.so" })
                if (NativeLibrary.TryLoad(candidate, asm, path, out var handle)) return handle;
            return IntPtr.Zero;
        });
    }
}