using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed class FpsModeManager
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private readonly IAppLogger _logger;
    private readonly StateHistoryStore _history;
    private readonly PowerPlanService _powerPlan = new();

    public FpsModeManager(IAppLogger logger, StateHistoryStore history)
    {
        _logger = logger;
        _history = history;
    }

    public async Task<FpsModeResult> EnableAsync(CancellationToken cancellationToken = default)
    {
        var applied = new List<string>();

        try
        {
            applied.Add(await _history.SaveFpsStateIfMissingAsync(cancellationToken).ConfigureAwait(false)
                ? "Snapshot de restauracao salvo"
                : "Snapshot anterior preservado");
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao salvar snapshot antes do Modo FPS.", ex);
        }

        try
        {
            if (await _powerPlan.SetActivePlanAsync(HighPerformanceGuid, cancellationToken).ConfigureAwait(false))
            {
                applied.Add("Plano de energia: Alto desempenho");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao alterar plano de energia.", ex);
        }

        try
        {
            RegistryTools.SetDWordValue(@"HKCU:\Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
            RegistryTools.SetDWordValue(@"HKCU:\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
            applied.Add("Modo Jogo do Windows: ativado");
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao ativar Modo Jogo.", ex);
        }

        try
        {
            RegistryTools.SetDWordValue(@"HKCU:\System\GameConfigStore", "GameDVR_Enabled", 0);
            applied.Add("Game DVR: desativado");
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao desativar Game DVR.", ex);
        }

        try
        {
            var dns = await NativeCommand.RunAsync("ipconfig.exe", "/flushdns", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (dns.Succeeded)
            {
                applied.Add("Cache DNS: limpo");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao limpar cache DNS.", ex);
        }

        _logger.Info($"Modo FPS aplicado: {applied.Count} ajuste(s).");
        return new FpsModeResult
        {
            Applied = applied,
            StatePath = _history.FpsStatePath
        };
    }

    public Task<RestoreModeResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        return _history.RestoreFpsSnapshotAsync(cancellationToken);
    }
}

