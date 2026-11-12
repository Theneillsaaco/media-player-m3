using System.ComponentModel;
using System.Diagnostics;

namespace Player.Storage;

public readonly record struct ToolResult(int ExitCode, string Output);

/// <summary>
/// Runs a short command and returns its output. The input, when there is one, travels on stdin (never as an
/// argument, so a secret never shows in a process list). Null when the program is not installed, fails to start or
/// takes longer than the timeout (it is killed then). Cancelling the token kills it and throws.
/// </summary>
public static class ProcessTool
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static async Task<ToolResult?> RunAsync(string file, IEnumerable<string> args, string? input = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        Process? process = null;

        try
        {
            process = Process.Start(info);
            if (process is null)
                return null;

            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), cts.Token);

            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errors = process.StandardError.ReadToEndAsync(cts.Token); // drained so it cannot block

            await process.WaitForExitAsync(cts.Token);
            await errors;

            return new ToolResult(process.ExitCode, output.Result);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process?.Kill(true);
            }
            catch
            {
                /* already gone */
            }

            if (ct.IsCancellationRequested)
                throw;

            return null; // timeout
        }
        finally
        {
            process?.Dispose();
        }
    }
}