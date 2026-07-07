using System.Text.Json;
using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;
using FreitasBoost.Core.SystemActions;

namespace FreitasBoost.AdminHelper;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task<int> Main(string[] args)
    {
        var requestPath = ReadOption(args, "--request");
        var responsePath = ReadOption(args, "--response");
        var logger = new FileAppLogger("freitas-boost-admin-helper");

        if (string.IsNullOrWhiteSpace(requestPath) || string.IsNullOrWhiteSpace(responsePath))
        {
            return 2;
        }

        try
        {
            var request = JsonSerializer.Deserialize<HelperRequest>(
                await File.ReadAllTextAsync(requestPath).ConfigureAwait(false),
                JsonOptions);

            if (request is null || string.IsNullOrWhiteSpace(request.Action))
            {
                await WriteResponseAsync(responsePath, HelperResponse.Fail("Requisicao invalida")).ConfigureAwait(false);
                return 3;
            }

            logger.Info($"Executando acao elevada: {request.Action}");
            var data = await DispatchAsync(request, logger).ConfigureAwait(false);
            await WriteResponseAsync(responsePath, HelperResponse.Success(data)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error("Falha no helper elevado.", ex);
            await WriteResponseAsync(responsePath, HelperResponse.Fail(ex.Message)).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<object> DispatchAsync(HelperRequest request, IAppLogger logger)
    {
        var history = new StateHistoryStore(logger);

        return request.Action switch
        {
            "clean-temp" => await new TempCleaner(logger).CleanAsync(
                request.Payload.Deserialize<CleanTempOptions>(JsonOptions) ?? new CleanTempOptions()).ConfigureAwait(false),

            "kill-processes" => await new ProcessManager(logger).KillAsync(
                request.Payload.Deserialize<List<KillProcessItem>>(JsonOptions) ?? []).ConfigureAwait(false),

            "fps-enable" => await new FpsModeManager(logger, history).EnableAsync().ConfigureAwait(false),

            "fps-restore" => await new FpsModeManager(logger, history).RestoreAsync().ConfigureAwait(false),

            "boost-all" => await new BoostAllManager(logger, history).RunAsync(
                request.Payload.Deserialize<BoostAllOptions>(JsonOptions) ?? new BoostAllOptions()).ConfigureAwait(false),

            "state-capture" => await history.CaptureAndSaveAsync("Estado manual", "manual").ConfigureAwait(false),

            "state-restore" => await history.RestoreStateAsync(
                request.Payload.Deserialize<IdPayload>(JsonOptions)?.Id ?? "").ConfigureAwait(false),

            "state-delete" => await history.DeleteStateAsync(
                request.Payload.Deserialize<IdPayload>(JsonOptions)?.Id ?? "").ConfigureAwait(false),

            _ => throw new InvalidOperationException($"Acao desconhecida: {request.Action}")
        };
    }

    private static async Task WriteResponseAsync(string path, HelperResponse response)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private sealed class HelperRequest
    {
        public string Action { get; set; } = "";
        public JsonElement Payload { get; set; }
    }

    private sealed class HelperResponse
    {
        public bool Ok { get; set; }
        public object? Data { get; set; }
        public string? Error { get; set; }

        public static HelperResponse Success(object data) => new() { Ok = true, Data = data };

        public static HelperResponse Fail(string error) => new() { Ok = false, Error = error };
    }

    private sealed class IdPayload
    {
        public string Id { get; set; } = "";
    }
}
