using System.Text.RegularExpressions;
using FreitasBoost.Core.Models;

namespace FreitasBoost.Core.SystemActions;

public sealed partial class Cs2ProfileAnalyzer
{
    private readonly PowerPlanService _powerPlan = new();

    public async Task<Cs2ProfileResult> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var gpuName = GetGpuName();
        var vendor = GetGpuVendor(gpuName);
        var steamPath = GetSteamPath();
        var cs2Path = FindCs2Exe(steamPath);
        var power = await _powerPlan.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);

        var gameMode = RegistryTools.GetDWordValue(@"HKCU:\Software\Microsoft\GameBar", "AutoGameModeEnabled");
        var gameDvr = RegistryTools.GetDWordValue(@"HKCU:\System\GameConfigStore", "GameDVR_Enabled");
        var hags = RegistryTools.GetDWordValue(@"HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");

        return new Cs2ProfileResult
        {
            GpuName = gpuName,
            GpuVendor = vendor,
            DriverVersion = GetGpuDriverVersion(),
            SteamPath = steamPath,
            Cs2Path = cs2Path,
            Cs2Detected = !string.IsNullOrWhiteSpace(cs2Path),
            PowerPlan = power.Name,
            GameMode = gameMode is null ? "desconhecido" : gameMode == 1 ? "ativado" : "desativado",
            GameDvr = gameDvr is null ? "desconhecido" : gameDvr == 0 ? "desativado" : "ativado",
            Hags = hags is null ? "desconhecido" : hags == 2 ? "ativado" : "desativado",
            Recommendations = BuildRecommendations(vendor)
        };
    }

    private static string GetGpuName()
    {
        var name = RegistryTools.GetStringValue(@"HKLM:\SYSTEM\CurrentControlSet\Control\Video", "HardwareInformation.AdapterString");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        // Fallback simples sem WMI: usa pnputil quando disponivel e extrai o primeiro adaptador de video.
        try
        {
            var result = Services.NativeCommand.RunAsync("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_VideoController | Sort-Object AdapterRAM -Descending | Select-Object -First 1).Name\"", timeoutMs: 8000)
                .GetAwaiter()
                .GetResult();
            var text = result.StandardOutput.Trim();
            return string.IsNullOrWhiteSpace(text) ? "GPU desconhecida" : text;
        }
        catch
        {
            return "GPU desconhecida";
        }
    }

    private static string GetGpuDriverVersion()
    {
        try
        {
            var result = Services.NativeCommand.RunAsync("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_VideoController | Sort-Object AdapterRAM -Descending | Select-Object -First 1).DriverVersion\"", timeoutMs: 8000)
                .GetAwaiter()
                .GetResult();
            var text = result.StandardOutput.Trim();
            return string.IsNullOrWhiteSpace(text) ? "desconhecido" : text;
        }
        catch
        {
            return "desconhecido";
        }
    }

    private static string? GetSteamPath()
    {
        var paths = new[]
        {
            @"HKCU:\Software\Valve\Steam",
            @"HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKLM:\SOFTWARE\Valve\Steam"
        };

        foreach (var path in paths)
        {
            var steamPath = RegistryTools.GetStringValue(path, "SteamPath");
            if (!string.IsNullOrWhiteSpace(steamPath) && Directory.Exists(steamPath))
            {
                return steamPath;
            }

            var installPath = RegistryTools.GetStringValue(path, "InstallPath");
            if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
            {
                return installPath;
            }
        }

        return null;
    }

    private static string? FindCs2Exe(string? steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return null;
        }

        var candidates = new List<string>
        {
            Path.Combine(steamPath, @"steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe")
        };

        var libraryFile = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            var content = File.ReadAllText(libraryFile);
            foreach (Match match in SteamLibraryPathRegex().Matches(content))
            {
                var libraryPath = match.Groups[1].Value.Replace(@"\\", @"\");
                if (!string.IsNullOrWhiteSpace(libraryPath))
                {
                    candidates.Add(Path.Combine(libraryPath, @"steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe"));
                }
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }

    private static string GetGpuVendor(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") || n.Contains("gtx")) return "nvidia";
        if (n.Contains("amd") || n.Contains("radeon") || n.Contains("rx ")) return "amd";
        if (n.Contains("intel") || n.Contains("arc")) return "intel";
        return "unknown";
    }

    private static List<Cs2Recommendation> BuildRecommendations(string vendor)
    {
        var list = new List<Cs2Recommendation>();

        if (vendor == "nvidia")
        {
            list.Add(Recommendation(
                "Latencia",
                "NVIDIA Reflex: Enabled + Boost",
                "Melhor custo-beneficio competitivo quando a prioridade e resposta do mouse/tiro.",
                "Pode custar alguns FPS e usar mais energia, mas a reducao de latencia costuma valer mais em CS2.",
                "Ative no CS2 em Video > Advanced Video > NVIDIA Reflex Low Latency.",
                1));
        }
        else if (vendor == "amd")
        {
            list.Add(Recommendation(
                "Latencia",
                "AMD Anti-Lag 2 no menu do CS2",
                "Boa troca quando o sistema esta GPU-bound ou com fila de renderizacao.",
                "Pode mexer em 1% lows em alguns PCs; teste ligado/desligado no mesmo mapa.",
                "Use a opcao integrada ao jogo, nunca tweaks externos que mexam no processo.",
                1));
        }
        else
        {
            list.Add(Recommendation(
                "Latencia",
                "Redutor de latencia do driver/jogo",
                "Priorize a opcao nativa do jogo ou driver da sua GPU.",
                "Sem GPU NVIDIA/AMD detectada, o app nao deve aplicar ajuste automatico.",
                "Valide no painel do fabricante e no menu Advanced Video do CS2.",
                1));
        }

        list.Add(Recommendation(
            "Frame pacing",
            "FPS maximo estavel, nao apenas FPS maximo possivel",
            "Melhora consistencia de mira quando o frametime fica estavel.",
            "Limitar FPS abaixo do pico pode reduzir media de FPS, mas melhora 1% low e sensacao.",
            "Use os sliders Maximum FPS In Game e Maximum FPS In Menus do CS2.",
            2));

        list.Add(Recommendation(
            "Windows",
            "HAGS: teste A/B por hardware",
            "Pode reduzir overhead de agendamento em alguns drivers.",
            "Tambem pode causar stutter em outros; nao e ajuste para aplicar cegamente.",
            "Teste uma partida com HAGS ligado e outra desligado, medindo 1% low e latencia.",
            3));

        list.Add(Recommendation(
            "Overlays",
            "Gravacao e overlays fora da partida competitiva",
            "Reduz processos disputando CPU/GPU e evita hitches de captura.",
            "Perde recursos de clipe/overlay enquanto joga.",
            "Deixe Game DVR desligado e feche overlays que nao usa no competitivo.",
            4));

        return list.OrderBy(static item => item.Priority).ToList();
    }

    private static Cs2Recommendation Recommendation(string category, string title, string impact, string tradeoff, string action, int priority)
    {
        return new Cs2Recommendation
        {
            Category = category,
            Title = title,
            Impact = impact,
            Tradeoff = tradeoff,
            Action = action,
            Priority = priority
        };
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex SteamLibraryPathRegex();
}

