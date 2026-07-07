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
        var system = await new SystemInfoProvider().GetAsync(cancellationToken).ConfigureAwait(false);
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
            LaunchOptions = GetLaunchOptions(steamPath),
            Benchmark = BuildBenchmark(system, gpuName, vendor, power.Name, gameDvr),
            Recommendations = BuildRecommendations(vendor, power.Name, gameMode, gameDvr, hags)
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

    private static string GetLaunchOptions(string? steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return "Steam nao detectado";
        }

        var userdata = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userdata))
        {
            return "nao detectado";
        }

        foreach (var file in Directory.GetFiles(userdata, "localconfig.vdf", SearchOption.AllDirectories))
        {
            try
            {
                var content = File.ReadAllText(file);
                if (!content.Contains("\"730\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = LaunchOptionsRegex().Match(content);
                if (match.Success)
                {
                    var value = match.Groups[1].Value.Replace("\\\"", "\"");
                    return string.IsNullOrWhiteSpace(value) ? "vazio" : value;
                }
            }
            catch
            {
                // Ignore Steam configs that cannot be read.
            }
        }

        return "nao detectado";
    }

    private static string GetGpuVendor(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") || n.Contains("gtx")) return "nvidia";
        if (n.Contains("amd") || n.Contains("radeon") || n.Contains("rx ")) return "amd";
        if (n.Contains("intel") || n.Contains("arc")) return "intel";
        return "unknown";
    }

    private static Cs2BenchmarkResult BuildBenchmark(
        SystemInfoResult system,
        string gpuName,
        string vendor,
        string powerPlan,
        int? gameDvr)
    {
        var cpuScore = ScoreCpu(system.Cpu);
        var gpuScore = ScoreGpu(gpuName, vendor);
        var ramGb = system.TotalMB / 1024d;
        var baseAverage = (int)Math.Round(70d + (cpuScore * 38d) + (gpuScore * 34d));
        var low = (int)Math.Round(baseAverage * (0.52 + Math.Min(cpuScore, gpuScore) * 0.015));
        var factors = new List<string>
        {
            $"CPU: {system.Cpu}",
            $"GPU: {gpuName}",
            $"RAM: {ramGb:0.#} GB",
            $"Energia: {powerPlan}",
            gameDvr == 0 ? "Game DVR desligado" : "Game DVR pode reduzir consistencia"
        };

        if (ramGb < 16)
        {
            baseAverage = (int)(baseAverage * 0.88);
            low = (int)(low * 0.82);
            factors.Add("Menos de 16 GB de RAM reduz folga para CS2 + Discord/Steam.");
        }
        else if (ramGb >= 32)
        {
            low = (int)(low * 1.04);
            factors.Add("32 GB+ ajuda 1% low quando ha apps em segundo plano.");
        }

        if (powerPlan.Contains("alto", StringComparison.OrdinalIgnoreCase) ||
            powerPlan.Contains("high", StringComparison.OrdinalIgnoreCase) ||
            powerPlan.Contains("ultimate", StringComparison.OrdinalIgnoreCase))
        {
            baseAverage = (int)(baseAverage * 1.04);
            low = (int)(low * 1.06);
        }

        if (gameDvr == 0)
        {
            low = (int)(low * 1.03);
        }

        var boostedAverage = (int)(baseAverage * 1.05);
        var boostedLow = (int)(low * 1.10);
        var confidence = vendor == "unknown" || gpuName.Contains("desconhecida", StringComparison.OrdinalIgnoreCase)
            ? "baixa"
            : "media";

        return new Cs2BenchmarkResult
        {
            Confidence = confidence,
            Basis = "Estimativa para 1080p competitivo/baixo, sem medir partida real.",
            CurrentAverageFps = ClampFps(baseAverage),
            CurrentOnePercentLowFps = ClampFps(low),
            BoostAverageFps = ClampFps(boostedAverage),
            BoostOnePercentLowFps = ClampFps(boostedLow),
            Factors = factors,
            Scenarios =
            [
                new Cs2BenchmarkScenario
                {
                    Name = "Atual",
                    AverageFps = ClampFps(baseAverage),
                    OnePercentLowFps = ClampFps(low),
                    Notes = "Configuracao detectada agora."
                },
                new Cs2BenchmarkScenario
                {
                    Name = "Com Boost",
                    AverageFps = ClampFps(boostedAverage),
                    OnePercentLowFps = ClampFps(boostedLow),
                    Notes = "Estimativa com plano alto desempenho, Game DVR off e memoria aparada."
                },
                new Cs2BenchmarkScenario
                {
                    Name = "Qualidade alta",
                    AverageFps = ClampFps((int)(baseAverage * 0.72)),
                    OnePercentLowFps = ClampFps((int)(low * 0.78)),
                    Notes = "Cenario visual mais pesado, ainda em 1080p."
                }
            ]
        };
    }

    private static int ScoreCpu(string cpu)
    {
        var text = cpu.ToLowerInvariant();
        if (text.Contains("i9") || text.Contains("ryzen 9")) return 9;
        if (text.Contains("i7") || text.Contains("ryzen 7")) return 7;
        if (text.Contains("i5") || text.Contains("ryzen 5")) return 5;
        if (text.Contains("i3") || text.Contains("ryzen 3")) return 3;
        return 4;
    }

    private static int ScoreGpu(string gpu, string vendor)
    {
        var text = gpu.ToLowerInvariant();
        if (text.Contains("4090") || text.Contains("4080") || text.Contains("7900")) return 10;
        if (text.Contains("4070") || text.Contains("3090") || text.Contains("3080") || text.Contains("7800")) return 8;
        if (text.Contains("4060") || text.Contains("3070") || text.Contains("3060") || text.Contains("7700") || text.Contains("6800")) return 6;
        if (text.Contains("3050") || text.Contains("2060") || text.Contains("1660") || text.Contains("7600") || text.Contains("6600")) return 4;
        if (vendor == "nvidia" || vendor == "amd") return 5;
        if (vendor == "intel") return 3;
        return 3;
    }

    private static int ClampFps(int value) => Math.Clamp(value, 45, 520);

    private static List<Cs2Recommendation> BuildRecommendations(string vendor, string powerPlan, int? gameMode, int? gameDvr, int? hags)
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
                "manual",
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
                "manual",
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
                "revisar",
                1));
        }

        list.Add(Recommendation(
            "Frame pacing",
            "FPS maximo estavel, nao apenas FPS maximo possivel",
            "Melhora consistencia de mira quando o frametime fica estavel.",
            "Limitar FPS abaixo do pico pode reduzir media de FPS, mas melhora 1% low e sensacao.",
            "Use os sliders Maximum FPS In Game e Maximum FPS In Menus do CS2.",
            "manual",
            2));

        list.Add(Recommendation(
            "Windows",
            "Plano de energia competitivo",
            powerPlan.Contains("alto", StringComparison.OrdinalIgnoreCase) || powerPlan.Contains("high", StringComparison.OrdinalIgnoreCase)
                ? "Plano ja favorece clocks altos durante a partida."
                : "Plano atual pode baixar clocks em momentos ruins.",
            "Usa mais energia e pode aquecer mais fora da tomada.",
            "Use o Modo FPS do Freitas Boost antes da partida e restaure depois.",
            powerPlan.Contains("alto", StringComparison.OrdinalIgnoreCase) || powerPlan.Contains("high", StringComparison.OrdinalIgnoreCase) ? "ok" : "aplicar",
            3));

        list.Add(Recommendation(
            "Windows",
            "HAGS: teste A/B por hardware",
            "Pode reduzir overhead de agendamento em alguns drivers.",
            "Tambem pode causar stutter em outros; nao e ajuste para aplicar cegamente.",
            "Teste uma partida com HAGS ligado e outra desligado, medindo 1% low e latencia.",
            hags is null ? "desconhecido" : "testar",
            4));

        list.Add(Recommendation(
            "Overlays",
            "Gravacao e overlays fora da partida competitiva",
            "Reduz processos disputando CPU/GPU e evita hitches de captura.",
            "Perde recursos de clipe/overlay enquanto joga.",
            "Deixe Game DVR desligado e feche overlays que nao usa no competitivo.",
            gameDvr == 0 ? "ok" : "aplicar",
            5));

        list.Add(Recommendation(
            "Windows",
            "Modo Jogo ativo",
            gameMode == 1 ? "Windows Game Mode ja esta ativo." : "Modo Jogo pode melhorar priorizacao do jogo.",
            "Em PCs raros vale comparar ligado/desligado.",
            "O Modo FPS ativa Game Mode e preserva rollback.",
            gameMode == 1 ? "ok" : "aplicar",
            6));

        return list.OrderBy(static item => item.Priority).ToList();
    }

    private static Cs2Recommendation Recommendation(string category, string title, string impact, string tradeoff, string action, string status, int priority)
    {
        return new Cs2Recommendation
        {
            Category = category,
            Title = title,
            Impact = impact,
            Tradeoff = tradeoff,
            Action = action,
            Status = status,
            Priority = priority
        };
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex SteamLibraryPathRegex();

    [GeneratedRegex("\"LaunchOptions\"\\s+\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LaunchOptionsRegex();
}
