using System.Text.Json;
using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed class StateHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppLogger _logger;
    private readonly PowerPlanService _powerPlan = new();

    public StateHistoryStore(IAppLogger logger)
    {
        _logger = logger;
    }

    public string StateDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Path.GetTempPath();
            }

            return Path.Combine(baseDir, "Freitas Boost");
        }
    }

    public string HistoryPath => Path.Combine(StateDir, "state-history.json");

    public string FpsStatePath => Path.Combine(StateDir, "fps-mode-state.json");

    public async Task<StateHistoryResult> ListAsync(CancellationToken cancellationToken = default)
    {
        var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
        return new StateHistoryResult
        {
            History = history,
            Path = HistoryPath,
            Current = await CaptureCurrentAsync("Estado atual", "current", cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<StateHistoryResult> CaptureAndSaveAsync(
        string label = "Estado manual",
        string source = "manual",
        CancellationToken cancellationToken = default)
    {
        var item = await CaptureCurrentAsync(label, source, cancellationToken).ConfigureAwait(false);
        var history = await AddHistoryItemAsync(item, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Estado salvo: {item.Id} ({item.PowerPlanName}).");

        return new StateHistoryResult
        {
            Item = item,
            History = history,
            Path = HistoryPath
        };
    }

    public async Task<bool> SaveFpsStateIfMissingAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(FpsStatePath))
        {
            return false;
        }

        Directory.CreateDirectory(StateDir);
        var snapshot = await CaptureCurrentAsync("Antes do Modo FPS", "automatico", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(FpsStatePath, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        await AddHistoryItemAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<StateHistoryResult> RestoreStateAsync(string id, CancellationToken cancellationToken = default)
    {
        var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
        var item = history.Items.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return new StateHistoryResult
            {
                Ok = false,
                Error = "Estado nao encontrado",
                History = history,
                Path = HistoryPath
            };
        }

        var restored = await RestoreSnapshotAsync(item, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Estado restaurado: {item.Id}.");

        return new StateHistoryResult
        {
            Item = item,
            Restored = restored,
            History = history,
            Path = HistoryPath
        };
    }

    public async Task<StateHistoryResult> DeleteStateAsync(string id, CancellationToken cancellationToken = default)
    {
        var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
        var before = history.Items.Count;
        history.Items = history.Items
            .Where(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await WriteHistoryAsync(history, cancellationToken).ConfigureAwait(false);
        var removed = before != history.Items.Count;
        _logger.Info($"Estado removido: {id}, removed={removed}.");

        return new StateHistoryResult
        {
            Removed = removed,
            History = history,
            Path = HistoryPath
        };
    }

    public async Task<StateHistoryResult> ImportSnapshotAsync(StateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Id))
        {
            snapshot.Id = NewStateId();
        }

        if (string.IsNullOrWhiteSpace(snapshot.Label))
        {
            snapshot.Label = "Estado importado";
        }

        snapshot.Source = string.IsNullOrWhiteSpace(snapshot.Source) ? "importado" : snapshot.Source;
        snapshot.CreatedAt = snapshot.CreatedAt == default ? DateTimeOffset.Now : snapshot.CreatedAt;
        snapshot.Registry ??= [];

        var history = await AddHistoryItemAsync(snapshot, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Estado importado: {snapshot.Id}.");

        return new StateHistoryResult
        {
            Item = snapshot,
            History = history,
            Path = HistoryPath
        };
    }

    public static string SerializeSnapshot(StateSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static StateSnapshot? DeserializeSnapshot(string json)
    {
        return JsonSerializer.Deserialize<StateSnapshot>(json, JsonOptions);
    }

    public async Task<RestoreModeResult> RestoreFpsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var result = new RestoreModeResult();

        if (File.Exists(FpsStatePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(FpsStatePath, cancellationToken).ConfigureAwait(false);
                var snapshot = JsonSerializer.Deserialize<StateSnapshot>(json, JsonOptions);
                if (snapshot is not null)
                {
                    result.StateUsed = true;
                    result.Restored = await RestoreSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    File.Delete(FpsStatePath);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Falha ao restaurar snapshot de FPS.", ex);
            }
        }

        const string balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        if (await _powerPlan.SetActivePlanAsync(balanced, cancellationToken).ConfigureAwait(false))
        {
            result.Restored.Add("Plano de energia: Equilibrado (fallback)");
        }

        try
        {
            RegistryTools.SetDWordValue(@"HKCU:\System\GameConfigStore", "GameDVR_Enabled", 1);
            result.Restored.Add("Game DVR: reativado (fallback)");
        }
        catch (Exception ex)
        {
            _logger.Error("Falha no fallback de Game DVR.", ex);
        }

        return result;
    }

    public async Task<StateSnapshot> CaptureCurrentAsync(
        string label,
        string source,
        CancellationToken cancellationToken = default)
    {
        var power = await _powerPlan.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);

        return new StateSnapshot
        {
            Id = NewStateId(),
            Label = label,
            Source = source,
            CreatedAt = DateTimeOffset.Now,
            PowerPlanGuid = power.Guid,
            PowerPlanName = power.Name,
            Registry =
            [
                RegistryTools.GetDWordState(@"HKCU:\Software\Microsoft\GameBar", "AllowAutoGameMode"),
                RegistryTools.GetDWordState(@"HKCU:\Software\Microsoft\GameBar", "AutoGameModeEnabled"),
                RegistryTools.GetDWordState(@"HKCU:\System\GameConfigStore", "GameDVR_Enabled")
            ]
        };
    }

    private async Task<List<string>> RestoreSnapshotAsync(StateSnapshot snapshot, CancellationToken cancellationToken)
    {
        var restored = new List<string>();

        if (!string.IsNullOrWhiteSpace(snapshot.PowerPlanGuid) &&
            await _powerPlan.SetActivePlanAsync(snapshot.PowerPlanGuid, cancellationToken).ConfigureAwait(false))
        {
            restored.Add("Plano de energia restaurado");
        }

        foreach (var entry in snapshot.Registry)
        {
            var message = RegistryTools.RestoreValue(entry);
            if (!string.IsNullOrWhiteSpace(message))
            {
                restored.Add(message);
            }
        }

        return restored;
    }

    private async Task<StateHistory> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(HistoryPath))
        {
            return new StateHistory();
        }

        try
        {
            var json = await File.ReadAllTextAsync(HistoryPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<StateHistory>(json, JsonOptions) ?? new StateHistory();
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao ler historico de estado.", ex);
            return new StateHistory();
        }
    }

    private async Task<StateHistory> AddHistoryItemAsync(StateSnapshot item, CancellationToken cancellationToken)
    {
        var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
        history.Items = new[] { item }
            .Concat(history.Items ?? [])
            .Take(25)
            .ToList();

        await WriteHistoryAsync(history, cancellationToken).ConfigureAwait(false);
        return history;
    }

    private async Task WriteHistoryAsync(StateHistory history, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StateDir);
        await File.WriteAllTextAsync(HistoryPath, JsonSerializer.Serialize(history, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NewStateId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"[..28];
    }
}
