using System.Text.RegularExpressions;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed partial class PowerPlanService
{
    private static readonly Regex GuidRegex = PowerPlanGuidRegex();
    private static readonly Regex NameRegex = PowerPlanNameRegex();

    public async Task<(string? Guid, string Name)> GetActivePlanAsync(CancellationToken cancellationToken = default)
    {
        var output = await NativeCommand.RunAsync("powercfg.exe", "/getactivescheme", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var text = string.Join(" ", output.StandardOutput, output.StandardError).Trim();
        var guid = GuidRegex.Match(text);
        var name = NameRegex.Match(text);

        return (
            guid.Success ? guid.Groups[1].Value : null,
            name.Success ? name.Groups[1].Value : "Desconhecido"
        );
    }

    public async Task<bool> SetActivePlanAsync(string guid, CancellationToken cancellationToken = default)
    {
        var result = await NativeCommand.RunAsync("powercfg.exe", $"/setactive {guid}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded;
    }

    [GeneratedRegex("([0-9a-fA-F-]{36})", RegexOptions.Compiled)]
    private static partial Regex PowerPlanGuidRegex();

    [GeneratedRegex("\\((.+?)\\)", RegexOptions.Compiled)]
    private static partial Regex PowerPlanNameRegex();
}

