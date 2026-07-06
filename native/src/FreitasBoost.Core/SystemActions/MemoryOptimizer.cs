using System.Diagnostics;
using System.Runtime.InteropServices;
using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed class MemoryOptimizer
{
    private readonly IAppLogger _logger;

    public MemoryOptimizer(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<MemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken = default)
    {
        var before = SystemInfoProvider.GetMemory();
        var trimmed = 0;

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                try
                {
                    if (EmptyWorkingSet(process.Handle))
                    {
                        trimmed++;
                    }
                }
                catch
                {
                    // Access denied and exited processes are expected here.
                }
            }
        }

        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        var after = SystemInfoProvider.GetMemory();
        var freed = Math.Max(0, before.UsedMB - after.UsedMB);

        _logger.Info($"RAM otimizada: {freed} MB liberados, {trimmed} processo(s).");
        return new MemoryOptimizeResult
        {
            TotalMB = before.TotalMB,
            BeforeUsedMB = before.UsedMB,
            AfterUsedMB = after.UsedMB,
            FreedMB = freed,
            ProcessesTrimmed = trimmed
        };
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}

