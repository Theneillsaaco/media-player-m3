using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Player.Core;

namespace Player.App.Services;

/// <summary>
/// Whether the desktop is in dark mode, read from the XDG settings portal (org.freedesktop.appearance /
/// color-scheme: 1 = dark, 2 = light, 0 = no preference) and kept current by listening for its
/// SettingChanged signal. The portal is read with busctl, or with gdbus when busctl is missing (inside a
/// Flatpak there is no busctl but gdbus comes with the runtime). Without either, it asks gsettings every few
/// seconds instead. Qt's own styleHints.colorScheme is not used because this project's Qt deploy leaves out the
/// platform theme plugins that feed it. Nothing here raises events: <see cref="IsDark"/> is read from the UI poll.
/// </summary>
public class SystemColorScheme : IDisposable
{
    /// <summary>Starts watching in the background. Safe to call again.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => WatchAsync(token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The value of the portal's reply: busctl prints "v u 1", gdbus prints "(&lt;uint32 1&gt;,)".
    /// 1 dark, 2 light (0 = no preference counts as light), else unknown.
    /// </summary>
    internal static int ParsePortal(string output)
    {
        var found = PortalValue.Match(output.Trim());
        if (!found.Success)
            return 0;

        return found.Groups[1].Value switch { "1" => 1, "0" or "2" => 2, _ => 0 };
    }

    /// <summary>gsettings prints 'prefer-dark', 'prefer-light' or 'default'.</summary>
    internal static int ParseGsettings(string output)
    {
        var value = output.Trim().Trim('\'');
        if (value.Contains("dark", StringComparison.OrdinalIgnoreCase)) return 1;
        if (value.Contains("light", StringComparison.OrdinalIgnoreCase) || value == "default") return 2;
        return 0;
    }

    private async Task WatchAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshAsync(token);
                await MonitorAsync(token); // returns when the monitor ends or cannot run
                await Task.Delay(Interval, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("theme", "system color scheme watcher stopped", ex);
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        var state = await ReadAsync(token);
        if (state != 0) // a failed read never turns a known answer back into "unknown"
            _state = state;
    }

    private static async Task<int> ReadAsync(CancellationToken token)
    {
        foreach (var method in new[] { "ReadOne", "Read" }) // Read is what portals older than version 2 have
        {
            // busctl first, then gdbus (the one a Flatpak has)
            string[][] attempts =
            [
                ["--user", "call", "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
                    "org.freedesktop.portal.Settings", method, "ss", "org.freedesktop.appearance", "color-scheme"],
                ["call", "--session", "--dest", "org.freedesktop.portal.Desktop",
                    "--object-path", "/org/freedesktop/portal/desktop",
                    "--method", "org.freedesktop.portal.Settings." + method,
                    "org.freedesktop.appearance", "color-scheme"]
            ];
            string[] tools = ["busctl", "gdbus"];
 
            for (var i = 0; i < tools.Length; i++)
                if (await RunAsync(tools[i], attempts[i], token) is { } output && ParsePortal(output) is var state and not 0)
                    return state;
        }
 
        if (await RunAsync("gsettings", ["get", "org.gnome.desktop.interface", "color-scheme"], token) is { } text)
            return ParseGsettings(text);
 
        return 0;
    }

    /// <summary>Watches the portal's signals (busctl, or gdbus when there is no busctl) and refreshes on a color-scheme change.</summary>
    private async Task MonitorAsync(CancellationToken token)
    {
        try
        {
            await MonitorWithAsync("busctl", ["--user", "monitor", "--match", Match], token);
            return;
        }
        catch (Win32Exception) { /* busctl is not installed: try gdbus */ }
 
        try
        {
            await MonitorWithAsync("gdbus", ["monitor", "--session", "--dest", "org.freedesktop.portal.Desktop"], token);
        }
        catch (Win32Exception) { /* neither is installed: the loop polls instead */ }
    }

    /// <summary>Runs one monitor command until it ends. Throws Win32Exception when the program is not installed.</summary>
    private async Task MonitorWithAsync(string file, string[] args, CancellationToken token)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is null)
                return;
            
            using var registration = token.Register(() => { try { process.Kill(true); } catch { /* already gone */ } });
            
            _ = process.StandardError.ReadToEndAsync(token); // drained so it cannot block
            
            while (await process.StandardOutput.ReadLineAsync(token) is { } line) 
                if (line.Contains("color-scheme", StringComparison.Ordinal))
                    await RefreshAsync(token);
        }
        catch (IOException) { }
        finally
        {
            process?.Dispose();
        }
    }
    
    /// <summary>Standard output of a finished command, or null when it is missing, fails or takes too long.</summary>
    private static async Task<string?> RunAsync(string file, string[] args, CancellationToken token)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        
        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is null) 
                return null;
            
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ToolTimeout);

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return process.ExitCode == 0 ? await output : null;
        }
        catch (Win32Exception) { return null; }
        catch (IOException) { return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            try { process?.Kill(true); } catch { /* already gone */ }
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }
    
    #region Fields

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    
    // busctl: "v u 1"   gdbus: "(<uint32 1>,)" or "(<<uint32 1>>,)"
    private static readonly Regex PortalValue = new(@"(?:\bu|uint32)\s+(\d+)", RegexOptions.Compiled);

    private const string Match =
        "type='signal',sender='org.freedesktop.portal.Desktop',interface='org.freedesktop.portal.Settings',member='SettingChanged'";

    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private volatile int _state; // 0 unknown, 1 dark, 2 light

    /// <summary>True for dark, false for light, null while nothing has answered.</summary>
    public bool? IsDark => _state switch { 1 => true, 2 => false, _ => null };

    #endregion
}