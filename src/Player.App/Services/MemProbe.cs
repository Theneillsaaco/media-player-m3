using Player.Core;

namespace Player.App.Services;

// cuesta nada si no está puesta:
//     PLAYER_MEMPROBE=1  player      cada 30 s escribe una línea en el log (~/.local/state/player/player.log)
//     PLAYER_MEMPROBE=gc player      igual, pero fuerza un GC antes de medir (el "managed live" es lo que de verdad vive)
// La línea separa lo que ve el sistema (rss, y de él anon / file / shmem) de lo que ve .NET (managed, heap), y lista
// los procesos hijos (el sidecar de Python, deno...). Lectura:
//   - rss sube y "managed live" no  -> es nativo: Qt (imágenes, texturas, shaders), mpv o QtMultimedia.
//   - "managed live" sube           -> es .NET: algo que se guarda y no se suelta.
//   - un hijo sube                  -> es el sidecar (yt-dlp), no la app.

/// <summary>Writes a memory line to the log every 30 seconds while PLAYER_MEMPROBE is set.</summary>
internal static class MemProbe
{
    private const double MiB = 1024.0 * 1024.0;

    public static void StartIfAsked()
    {
        var mode = Environment.GetEnvironmentVariable("PLAYER_MEMPROBE");
        if (string.IsNullOrEmpty(mode) || !OperatingSystem.IsLinux())
            return;

        var collect = mode.Equals("gc", StringComparison.OrdinalIgnoreCase);

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try { Log.Info("mem", Sample(collect)); }
                catch (Exception ex) { Log.Warn("mem", "sample", ex); }

                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        });
    }

    private static string Sample(bool collect)
    {
        if (collect)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        var info = GC.GetGCMemoryInfo();
        var status = Status("/proc/self/status");

        return $"rss={Mb(status, "VmRSS")} (anon={Mb(status, "RssAnon")} file={Mb(status, "RssFile")} shmem={Mb(status, "RssShmem")}) MB"
            + $" threads={status.GetValueOrDefault("Threads", "?")}"
            + $" | managed live={GC.GetTotalMemory(false) / MiB:0} heap={info.HeapSizeBytes / MiB:0} committed={info.TotalCommittedBytes / MiB:0} fragmented={info.FragmentedBytes / MiB:0} MB"
            + $" gc0/1/2={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"
            + " | children: " + Children();
    }

    private static string Children()
    {
        List<string> found = [];

        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out _))
                continue;

            try
            {
                var status = Status(Path.Combine(dir, "status"));
                if (status.GetValueOrDefault("PPid") == Environment.ProcessId.ToString())
                    found.Add($"{status.GetValueOrDefault("Name", "?")}:{Mb(status, "VmRSS")}");
            }
            catch
            {
                // the process ended while it was read
            }
        }

        return found.Count == 0 ? "none" : string.Join(" ", found) + " MB";
    }

    private static Dictionary<string, string> Status(string path)
    {
        Dictionary<string, string> values = [];

        foreach (var line in File.ReadLines(path))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                values[line[..colon]] = line[(colon + 1)..].Trim();
        }

        return values;
    }

    /// <summary>"123456 kB" as megabytes.</summary>
    private static string Mb(Dictionary<string, string> status, string key) =>
        status.TryGetValue(key, out var text) && long.TryParse(text.Split(' ')[0], out var kb) ? (kb / 1024.0).ToString("0") : "?";
}
