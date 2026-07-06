using System.Diagnostics;
using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed class ProcessManager
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "MemCompression", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "svchost", "fontdrvhost", "dwm", "explorer",
        "spoolsv", "SearchHost", "SearchIndexer", "ShellExperienceHost",
        "StartMenuExperienceHost", "sihost", "ctfmon", "RuntimeBroker", "dllhost",
        "conhost", "WmiPrvSE", "audiodg", "taskhostw", "SecurityHealthService",
        "SecurityHealthSystray", "MsMpEng", "NisSrv", "powershell", "pwsh",
        "electron", "Freitas Boost", "freitas-boost", "FreitasBoost.App",
        "FreitasBoost.AdminHelper", "TextInputHost", "LockApp", "wlanext",
        "WUDFHost", "steam", "steamwebhelper", "cs2"
    };

    private readonly IAppLogger _logger;

    public ProcessManager(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<ProcessListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        var groups = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = "";
            try { name = process.ProcessName; } catch { }

            if (string.IsNullOrWhiteSpace(name) || ProtectedNames.Contains(name))
            {
                process.Dispose();
                continue;
            }

            if (!groups.TryGetValue(name, out var list))
            {
                list = [];
                groups[name] = list;
            }

            list.Add(process);
        }

        var candidates = new List<ProcessCandidate>();
        foreach (var (name, processes) in groups)
        {
            double mem = 0;
            var pids = new List<int>();
            var hasWindow = false;

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        mem += process.WorkingSet64 / 1024d / 1024d;
                        pids.Add(process.Id);
                        hasWindow |= process.MainWindowHandle != IntPtr.Zero;
                    }
                    catch
                    {
                        // Exited processes are ignored.
                    }
                }
            }

            if (mem >= 25)
            {
                candidates.Add(new ProcessCandidate
                {
                    Name = name,
                    MemMB = Math.Round(mem, 1),
                    Count = pids.Count,
                    Pids = pids,
                    HasWindow = hasWindow
                });
            }
        }

        var result = new ProcessListResult
        {
            Processes = candidates
                .OrderByDescending(static item => item.MemMB)
                .Take(25)
                .ToList()
        };

        _logger.Info($"Processos analisados: {result.Processes.Count} candidato(s).");
        return Task.FromResult(result);
    }

    public Task<KillProcessesResult> KillAsync(IEnumerable<KillProcessItem> items, CancellationToken cancellationToken = default)
    {
        var result = new KillProcessesResult();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Name) || ProtectedNames.Contains(item.Name))
            {
                continue;
            }

            var pids = item.Pids.Count > 0
                ? item.Pids
                : Process.GetProcessesByName(item.Name).Select(static process => process.Id).ToList();

            foreach (var pid in pids)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (ProtectedNames.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    if (!string.Equals(process.ProcessName, item.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Failed.Add($"{item.Name} (PID {pid} mudou)");
                        continue;
                    }

                    process.Kill(entireProcessTree: false);
                    result.Killed.Add(new KilledProcess { Name = item.Name, Pid = pid });
                }
                catch
                {
                    result.Failed.Add($"{item.Name} (PID {pid})");
                }
            }
        }

        _logger.Info($"Encerramento de processos: {result.Killed.Count} encerrado(s), {result.Failed.Count} falha(s).");
        return Task.FromResult(result);
    }
}

