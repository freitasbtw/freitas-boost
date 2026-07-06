using System.Runtime.InteropServices;
using System.Security.Principal;
using FreitasBoost.Core.Models;

namespace FreitasBoost.Core.SystemActions;

public sealed class SystemInfoProvider
{
    private readonly PowerPlanService _powerPlan = new();

    public async Task<SystemInfoResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var memory = GetMemory();
        var power = await _powerPlan.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);

        return new SystemInfoResult
        {
            TotalMB = memory.TotalMB,
            FreeMB = memory.FreeMB,
            UsedMB = memory.UsedMB,
            UsedPct = memory.UsedPct,
            Cpu = GetCpuName(),
            PowerPlan = power.Name,
            IsAdmin = IsAdministrator()
        };
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static (long TotalMB, long FreeMB, long UsedMB, int UsedPct) GetMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            return (0, 0, 0, 0);
        }

        var total = (long)Math.Round(status.ullTotalPhys / 1024d / 1024d);
        var free = (long)Math.Round(status.ullAvailPhys / 1024d / 1024d);
        var used = Math.Max(0, total - free);
        var pct = total > 0 ? (int)Math.Round((double)used / total * 100) : 0;
        return (total, free, used, pct);
    }

    private static string GetCpuName()
    {
        const string cpuKey = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        return Microsoft.Win32.Registry.GetValue(cpuKey, "ProcessorNameString", null)?.ToString()?.Trim()
            ?? "Processador desconhecido";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}

