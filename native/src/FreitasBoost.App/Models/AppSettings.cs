namespace FreitasBoost.App.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool RequireBackupBeforeSensitiveAction { get; set; } = true;
    public bool DeepCleanByDefault { get; set; }
    public bool SkipOnboarding { get; set; }
    public string PerformanceProfile { get; set; } = "Competitivo";
    public List<string> NeverKillProcesses { get; set; } = [];
    public List<string> SuggestedKillProcesses { get; set; } = [];
}
