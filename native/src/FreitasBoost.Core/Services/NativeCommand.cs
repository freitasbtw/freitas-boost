using System.Diagnostics;

namespace FreitasBoost.Core.Services;

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public bool Succeeded => ExitCode == 0;
}

public static class NativeCommand
{
    public static async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        int timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{fileName} excedeu {timeoutMs} ms.");
        }

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdout.ConfigureAwait(false),
            StandardError = await stderr.ConfigureAwait(false)
        };
    }
}

