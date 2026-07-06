using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FreitasBoost.Core.Services;
using FreitasBoost.Core.SystemActions;

namespace FreitasBoost.App.Services;

public sealed class AdminActionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAppLogger _logger;

    public AdminActionClient(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<T> RunAsync<T>(string action, object? payload = null, CancellationToken cancellationToken = default)
    {
        var helperPath = ResolveHelperPath();
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("Helper elevado nao encontrado. Compile a solucao nativa primeiro.", helperPath);
        }

        var workDir = Path.Combine(Path.GetTempPath(), "FreitasBoost", "admin");
        Directory.CreateDirectory(workDir);
        var id = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(workDir, $"{id}.request.json");
        var responsePath = Path.Combine(workDir, $"{id}.response.json");

        var request = new
        {
            action,
            payload = payload ?? new { }
        };

        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), cancellationToken)
            .ConfigureAwait(false);

        var arguments = $"--request \"{requestPath}\" --response \"{responsePath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? AppContext.BaseDirectory,
            Verb = SystemInfoProvider.IsAdministrator() ? "" : "runas"
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Nao foi possivel iniciar o helper elevado.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Elevacao cancelada pelo usuario.", ex, cancellationToken);
        }

        if (!File.Exists(responsePath))
        {
            throw new InvalidOperationException("O helper elevado nao retornou resposta.");
        }

        var responseJson = await File.ReadAllTextAsync(responsePath, cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<HelperResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Resposta invalida do helper elevado.");

        TryDelete(requestPath);
        TryDelete(responsePath);

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Acao elevada falhou.");
        }

        var data = response.Data.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException("Resposta elevada sem dados.");

        _logger.Info($"Acao elevada concluida: {action}.");
        return data;
    }

    private static string ResolveHelperPath()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "AdminHelper", "FreitasBoost.AdminHelper.exe");
        if (File.Exists(packaged))
        {
            return packaged;
        }

        return Path.Combine(AppContext.BaseDirectory, "FreitasBoost.AdminHelper.exe");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private sealed class HelperResponse
    {
        public bool Ok { get; set; }
        public JsonElement Data { get; set; }
        public string? Error { get; set; }
    }
}

