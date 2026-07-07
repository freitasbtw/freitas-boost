using System.Text.Json;
using FreitasBoost.App.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.App.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppLogger _logger;

    public AppSettingsStore(IAppLogger logger)
    {
        _logger = logger;
    }

    public string SettingsPath
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Path.GetTempPath();
            }

            return Path.Combine(baseDir, "Freitas Boost", "app-settings.json");
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao carregar configuracoes.", ex);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        settings.NeverKillProcesses = Normalize(settings.NeverKillProcesses);
        settings.SuggestedKillProcesses = Normalize(settings.SuggestedKillProcesses);

        await File.WriteAllTextAsync(
                SettingsPath,
                JsonSerializer.Serialize(settings, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static List<string> ParseProcessList(string text)
    {
        return Normalize((text ?? "")
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static string FormatProcessList(IEnumerable<string> items)
    {
        return string.Join(", ", Normalize(items));
    }

    private static List<string> Normalize(IEnumerable<string> items)
    {
        return items
            .Select(static item => item.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
