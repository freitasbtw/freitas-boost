using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed class BoostAllManager
{
    private readonly IAppLogger _logger;
    private readonly StateHistoryStore _history;
    private readonly SystemInfoProvider _systemInfo = new();

    public BoostAllManager(IAppLogger logger, StateHistoryStore history)
    {
        _logger = logger;
        _history = history;
    }

    public async Task<BoostAllResult> RunAsync(BoostAllOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new BoostAllOptions();
        var result = new BoostAllResult();

        try
        {
            result.Before = await _systemInfo.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Status inicial indisponivel");
            _logger.Error("Falha ao ler status antes do boost.", ex);
        }

        try
        {
            result.Clean = await new TempCleaner(_logger)
                .CleanAsync(options.Clean, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Limpeza falhou");
            _logger.Error("Falha na limpeza durante boost.", ex);
        }

        try
        {
            result.Memory = await new MemoryOptimizer(_logger)
                .OptimizeAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Otimizacao de RAM falhou");
            _logger.Error("Falha na RAM durante boost.", ex);
        }

        try
        {
            result.Fps = await new FpsModeManager(_logger, _history)
                .EnableAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Modo FPS falhou");
            _logger.Error("Falha no Modo FPS durante boost.", ex);
        }

        try
        {
            result.After = await _systemInfo.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Status final indisponivel");
            _logger.Error("Falha ao ler status depois do boost.", ex);
        }

        result.Ok = result.Warnings.Count == 0;
        _logger.Info($"Boost consolidado concluido: ok={result.Ok}, warnings={result.Warnings.Count}.");
        return result;
    }
}
