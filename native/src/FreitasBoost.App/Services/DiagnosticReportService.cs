using System.Reflection;
using System.Text;
using FreitasBoost.App.Models;
using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.App.Services;

public sealed class DiagnosticReportService
{
    private readonly FileAppLogger _logger;

    public DiagnosticReportService(FileAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<string> BuildAsync(
        AppSettings settings,
        SystemInfoResult? system,
        StateHistoryResult? history,
        Cs2ProfileResult? cs2,
        string settingsPath,
        CancellationToken cancellationToken = default)
    {
        var report = new StringBuilder();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

        report.AppendLine("Freitas Boost - diagnostico");
        report.AppendLine($"Versao: {version}");
        report.AppendLine($"Data: {DateTimeOffset.Now:O}");
        report.AppendLine();

        report.AppendLine("[Sistema]");
        report.AppendLine($"CPU: {system?.Cpu ?? "indisponivel"}");
        report.AppendLine($"RAM: {FormatMemory(system?.UsedMB ?? 0)} usada de {FormatMemory(system?.TotalMB ?? 0)} ({system?.UsedPct ?? 0}%)");
        report.AppendLine($"Plano de energia: {system?.PowerPlan ?? "indisponivel"}");
        report.AppendLine($"Permissao: {(system?.IsAdmin == true ? "Administrador" : "UAC sob demanda")}");
        report.AppendLine();

        report.AppendLine("[Configuracoes]");
        report.AppendLine($"Perfil: {settings.PerformanceProfile}");
        report.AppendLine($"Backup obrigatorio: {settings.RequireBackupBeforeSensitiveAction}");
        report.AppendLine($"Limpeza profunda padrao: {settings.DeepCleanByDefault}");
        report.AppendLine($"Pular onboarding: {settings.SkipOnboarding}");
        report.AppendLine($"Nunca encerrar: {string.Join(", ", settings.NeverKillProcesses)}");
        report.AppendLine($"Sugerir encerrar: {string.Join(", ", settings.SuggestedKillProcesses)}");
        report.AppendLine($"Arquivo: {settingsPath}");
        report.AppendLine();

        report.AppendLine("[Rollback]");
        report.AppendLine($"Backups locais: {history?.History.Items.Count ?? 0}");
        report.AppendLine($"Historico: {history?.Path ?? "indisponivel"}");
        report.AppendLine();

        report.AppendLine("[CS2]");
        report.AppendLine($"GPU: {cs2?.GpuName ?? "indisponivel"}");
        report.AppendLine($"Driver: {cs2?.DriverVersion ?? "indisponivel"}");
        report.AppendLine($"Steam: {cs2?.SteamPath ?? "nao detectado"}");
        report.AppendLine($"CS2: {cs2?.Cs2Path ?? "nao detectado"}");
        report.AppendLine($"Game Mode: {cs2?.GameMode ?? "desconhecido"}");
        report.AppendLine($"Game DVR: {cs2?.GameDvr ?? "desconhecido"}");
        report.AppendLine($"HAGS: {cs2?.Hags ?? "desconhecido"}");
        report.AppendLine($"Estimativa CS2 atual: {cs2?.Benchmark.CurrentAverageFps ?? 0} FPS medio / {cs2?.Benchmark.CurrentOnePercentLowFps ?? 0} FPS 1% low");
        report.AppendLine($"Estimativa CS2 com boost: {cs2?.Benchmark.BoostAverageFps ?? 0} FPS medio / {cs2?.Benchmark.BoostOnePercentLowFps ?? 0} FPS 1% low");
        report.AppendLine();

        report.AppendLine("[Logs recentes]");
        foreach (var line in await ReadRecentLogsAsync(cancellationToken).ConfigureAwait(false))
        {
            report.AppendLine(line);
        }

        return report.ToString();
    }

    private async Task<IEnumerable<string>> ReadRecentLogsAsync(CancellationToken cancellationToken)
    {
        var logDir = Path.GetDirectoryName(_logger.LogPath);
        if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
        {
            return ["Sem logs locais."];
        }

        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(logDir, "*.log").OrderBy(Path.GetFileName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var recent = (await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false))
                    .TakeLast(12);
                lines.Add($"# {Path.GetFileName(file)}");
                lines.AddRange(recent);
            }
            catch (Exception ex)
            {
                lines.Add($"# {Path.GetFileName(file)}: leitura falhou ({ex.GetType().Name})");
            }
        }

        return lines.Count > 0 ? lines : ["Sem logs locais."];
    }

    private static string FormatMemory(long mb)
    {
        return mb >= 1024 ? $"{mb / 1024d:0.0} GB" : $"{mb} MB";
    }
}
