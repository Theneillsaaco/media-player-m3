using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Player.Storage.Secrets;

/// <summary>
/// Keeps small secrets (a session key). The Secret Service through secret-tool comes first;
/// when it is missing or no keyring daemon answers, a file with mode 600 under the config folder.
/// The secret always travels on stdin, never as an argument, so it never shows in a process list.
/// </summary>
public  sealed class SecretStore(string application = "player")
{
    public async Task<string> GetAsync(string name)
    {
        var (ok, output) = await RunTool(["lookup", "application", application, "account", name], null);
        
        if (ok && output.Trim() is { Length: > 0} secret)
            return secret;
        
        var path = FilePath(name);
        
        return File.Exists(path) ? (await File.ReadAllTextAsync(path)).Trim() : null;
    }

    public async Task Set(string name, string value)
    {
        var (ok, _) = await RunTool(["store", $"--label=Player ({name})", "application", application, "account", name], value);

        if (ok)
        {
            DeleteFile(name); // the keyring has it now: drop any older fallback file
            return;
        }

        var path = AppPaths.Prepare(FilePath(name));
        File.Delete(path);

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        
        await stream.WriteAsync(Encoding.UTF8.GetBytes(value));
    }
    
    public async Task Delete(string name)
    {
        await RunTool(["clear", "application", application, "account", name], null);
        DeleteFile(name);
    }
    
    /// <summary>File.Delete throws when the folder is missing; File.Exists just says no.</summary>
    private static void DeleteFile(string name)
    {
        var path = FilePath(name);
        if (File.Exists(path)) File.Delete(path);
    }
    
    private static string FilePath(string name)
    {
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("Invalid secret name", nameof(name));

        return AppPaths.ConfigFile(Path.Combine("secrets", name));
    }
    
    private static async Task<(bool Ok, string Output)> RunTool(string[] args, string? input)
    {
        var info = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        Process? process = null;

        try
        {
            process = Process.Start(info);
            
            if (process is null)
                return (false, "");
            
            using var timeout = new CancellationTokenSource(ToolTimeout);

            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            
            process.StandardInput.Close();
            
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors  = process.StandardError.ReadToEndAsync(timeout.Token); // drained so it cannot block
            
            await process.WaitForExitAsync(timeout.Token);
            await errors;

            return (process.ExitCode == 0, await output);
        }
        catch (Win32Exception) // secret-tool is not installed
        {
            return (false, "");
        }
        catch (OperationCanceledException)
        {
            try { process?.Kill(true); } catch { /* already gone */ }
            return (false, "");
        }
        catch (IOException)
        {
            return (false, "");
        }
        finally
        {
            process?.Dispose();
        }
    }
    
    #region Fields

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(10);

    #endregion
}