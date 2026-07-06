namespace FreitasBoost.Core.Models;

public sealed class SystemInfoResult
{
    public bool Ok { get; set; } = true;
    public long TotalMB { get; set; }
    public long FreeMB { get; set; }
    public long UsedMB { get; set; }
    public int UsedPct { get; set; }
    public string Cpu { get; set; } = "Processador desconhecido";
    public string PowerPlan { get; set; } = "Desconhecido";
    public bool IsAdmin { get; set; }
}

public sealed class CleanTempOptions
{
    public bool DeepClean { get; set; }
}

public sealed class CleanTargetDetail
{
    public string Path { get; set; } = "";
    public double FreedMB { get; set; }
}

public sealed class CleanTempResult
{
    public bool Ok { get; set; } = true;
    public bool DeepClean { get; set; }
    public long FreedBytes { get; set; }
    public double FreedMB { get; set; }
    public int FilesRemoved { get; set; }
    public bool RecycleBin { get; set; }
    public List<string> Skipped { get; set; } = [];
    public List<CleanTargetDetail> Details { get; set; } = [];
}

public sealed class MemoryOptimizeResult
{
    public bool Ok { get; set; } = true;
    public long TotalMB { get; set; }
    public long BeforeUsedMB { get; set; }
    public long AfterUsedMB { get; set; }
    public long FreedMB { get; set; }
    public int ProcessesTrimmed { get; set; }
}

public class ProcessCandidate
{
    public string Name { get; set; } = "";
    public double MemMB { get; set; }
    public int Count { get; set; }
    public List<int> Pids { get; set; } = [];
    public bool HasWindow { get; set; }
}

public sealed class ProcessListResult
{
    public bool Ok { get; set; } = true;
    public List<ProcessCandidate> Processes { get; set; } = [];
}

public sealed class KillProcessItem
{
    public string Name { get; set; } = "";
    public List<int> Pids { get; set; } = [];
}

public sealed class KilledProcess
{
    public string Name { get; set; } = "";
    public int Pid { get; set; }
}

public sealed class KillProcessesResult
{
    public bool Ok { get; set; } = true;
    public List<KilledProcess> Killed { get; set; } = [];
    public List<string> Failed { get; set; } = [];
}

public sealed class RegistryValueState
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Exists { get; set; }
    public int? Value { get; set; }
    public string Type { get; set; } = "DWord";
}

public sealed class StateSnapshot
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "Estado salvo";
    public string Source { get; set; } = "local";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string? PowerPlanGuid { get; set; }
    public string PowerPlanName { get; set; } = "Desconhecido";
    public List<RegistryValueState> Registry { get; set; } = [];
}

public sealed class StateHistory
{
    public int Version { get; set; } = 1;
    public List<StateSnapshot> Items { get; set; } = [];
}

public sealed class StateHistoryResult
{
    public bool Ok { get; set; } = true;
    public StateHistory History { get; set; } = new();
    public string Path { get; set; } = "";
    public StateSnapshot? Current { get; set; }
    public StateSnapshot? Item { get; set; }
    public bool Removed { get; set; }
    public List<string> Restored { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class FpsModeResult
{
    public bool Ok { get; set; } = true;
    public List<string> Applied { get; set; } = [];
    public string StatePath { get; set; } = "";
}

public sealed class RestoreModeResult
{
    public bool Ok { get; set; } = true;
    public List<string> Restored { get; set; } = [];
    public bool StateUsed { get; set; }
}

public sealed class Cs2Recommendation
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Impact { get; set; } = "";
    public string Tradeoff { get; set; } = "";
    public string Action { get; set; } = "";
    public int Priority { get; set; }
}

public sealed class Cs2ProfileResult
{
    public bool Ok { get; set; } = true;
    public string GpuName { get; set; } = "GPU desconhecida";
    public string GpuVendor { get; set; } = "unknown";
    public string DriverVersion { get; set; } = "desconhecido";
    public string? SteamPath { get; set; }
    public string? Cs2Path { get; set; }
    public bool Cs2Detected { get; set; }
    public string PowerPlan { get; set; } = "Desconhecido";
    public string GameMode { get; set; } = "desconhecido";
    public string GameDvr { get; set; } = "desconhecido";
    public string Hags { get; set; } = "desconhecido";
    public List<Cs2Recommendation> Recommendations { get; set; } = [];
}
