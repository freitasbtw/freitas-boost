using System.Collections.ObjectModel;
using System.Text.Json;
using FreitasBoost.App.Models;
using FreitasBoost.App.Services;
using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;
using FreitasBoost.Core.SystemActions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace FreitasBoost.App.UI;

public sealed partial class MainWindow : Window
{
    private readonly FileAppLogger _logger = new("freitas-boost-native");
    private readonly SystemInfoProvider _systemInfo = new();
    private readonly AdminActionClient _admin;
    private readonly MemoryOptimizer _memory;
    private readonly ProcessManager _processes;
    private readonly StateHistoryStore _history;
    private readonly TempCleaner _cleanPreview;
    private readonly AppSettingsStore _settingsStore;
    private readonly DiagnosticReportService _diagnostics;
    private readonly Cs2ProfileAnalyzer _cs2 = new();
    private readonly DispatcherTimer _refreshTimer = new();

    private readonly ObservableCollection<ProcessCandidateView> _processItems = [];
    private readonly ObservableCollection<StateSnapshotView> _stateItems = [];
    private AppSettings _settings = new();
    private SystemInfoResult? _lastSystemInfo;
    private StateHistoryResult? _lastHistory;
    private Cs2ProfileResult? _lastCs2Profile;
    private bool _settingsLoaded;

    public MainWindow()
    {
        InitializeComponent();

        _admin = new AdminActionClient(_logger);
        _memory = new MemoryOptimizer(_logger);
        _processes = new ProcessManager(_logger);
        _history = new StateHistoryStore(_logger);
        _cleanPreview = new TempCleaner(_logger);
        _settingsStore = new AppSettingsStore(_logger);
        _diagnostics = new DiagnosticReportService(_logger);

        ExtendsContentIntoTitleBar = true;
        ProcessList.ItemsSource = _processItems;
        StateHistoryList.ItemsSource = _stateItems;
        BeginOnboardingScan();
        SetActionProgress(false);
        ShowPage("Optimize");
        AddLog("info", "Status", "Aguardando uma otimizacao");

        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += async (_, _) => await LoadSystemInfoAsync();

        Activated += async (_, _) =>
        {
            if (!_settingsLoaded)
            {
                await LoadSettingsAsync();
            }

            await LoadSystemInfoAsync();
            await LoadStateHistoryAsync();
            _refreshTimer.Start();
        };
    }

    private async void OnAdminChipClick(object sender, RoutedEventArgs e)
    {
        if (SystemInfoProvider.IsAdministrator())
        {
            return;
        }

        ShowStatus("Permissao de administrador", "As acoes sensiveis solicitam UAC apenas quando executadas.", InfoBarSeverity.Informational);
        await Task.CompletedTask;
    }

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page })
        {
            ShowPage(page);
        }
    }

    private void OnShowOnboardingClick(object sender, RoutedEventArgs e)
    {
        ShowEntryScreen();
    }

    private async void OnOnboardingSaveBackupClick(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync(OnboardingBackupButton, async () =>
        {
            var result = await _history.CaptureAndSaveAsync("Primeiro backup", "onboarding");
            RenderHistory(result);
            ShowStatus("Backup salvo", "Snapshot inicial criado para rollback.", InfoBarSeverity.Success);
        });
    }

    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        await SaveSettingsFromUiAsync();
        ShowStatus("Configuracoes salvas", "Preferencias aplicadas ao fluxo do app.", InfoBarSeverity.Success);
    }

    private void OnCleanModeToggled(object sender, RoutedEventArgs e)
    {
        RenderCleanPreviewHint();
    }

    private async void OnPreviewCleanClick(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync(CleanPreviewButton, async () =>
        {
            var preview = await _cleanPreview.PreviewAsync(BuildCleanOptions());
            CleanPreviewText.Text = FormatCleanPreview(preview);
            ReplaceLog(new[]
            {
                ("info", "Preview", $"{preview.FilesRemoved} arquivo(s), {preview.FreedMB:0.#} MB"),
                ("info", "Modo", preview.DeepClean ? "profundo" : "seguro")
            }.Concat(preview.Details.Take(4).Select(item => ("info", "Alvo", $"{item.Path}: {item.FreedMB:0.#} MB"))));
        });
    }

    private async void OnCopyDiagnosticClick(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync(CopyDiagnosticButton, async () =>
        {
            var report = await _diagnostics.BuildAsync(
                _settings,
                _lastSystemInfo,
                _lastHistory,
                _lastCs2Profile,
                _settingsStore.SettingsPath);
            CopyTextToClipboard(report);
            ShowStatus("Diagnostico copiado", "Relatorio tecnico copiado para a area de transferencia.", InfoBarSeverity.Success);
        });
    }

    private async void OnEnterAppClick(object sender, RoutedEventArgs e)
    {
        await SaveOnboardingChoicesAsync();
        EnterApp();
    }

    private void EnterApp()
    {
        OnboardingScreen.Visibility = Visibility.Collapsed;
        AppShell.Visibility = Visibility.Visible;
        ShowPage("Optimize");
    }

    private void ShowEntryScreen()
    {
        AppShell.Visibility = Visibility.Collapsed;
        OnboardingScreen.Visibility = Visibility.Visible;
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        _settingsLoaded = true;
        ApplySettingsToUi();

        if (_settings.SkipOnboarding)
        {
            EnterApp();
        }
        else
        {
            ShowEntryScreen();
        }
    }

    private void ApplySettingsToUi()
    {
        RequireBackupToggle.IsOn = _settings.RequireBackupBeforeSensitiveAction;
        DeepCleanDefaultToggle.IsOn = _settings.DeepCleanByDefault;
        SkipOnboardingToggle.IsOn = _settings.SkipOnboarding;
        CleanDeepToggle.IsOn = _settings.DeepCleanByDefault;
        NeverKillTextBox.Text = AppSettingsStore.FormatProcessList(_settings.NeverKillProcesses);
        SuggestedKillTextBox.Text = AppSettingsStore.FormatProcessList(_settings.SuggestedKillProcesses);
        SelectComboItem(SettingsProfileCombo, _settings.PerformanceProfile);
        SelectComboItem(OnboardingProfileCombo, _settings.PerformanceProfile);
        OnboardingBackupToggle.IsOn = _settings.RequireBackupBeforeSensitiveAction;
        OnboardingDeepCleanToggle.IsOn = _settings.DeepCleanByDefault;
        OnboardingSkipToggle.IsOn = _settings.SkipOnboarding || !_settingsLoaded;
        RenderCleanPreviewHint();
    }

    private async Task SaveOnboardingChoicesAsync()
    {
        _settings.PerformanceProfile = GetComboText(OnboardingProfileCombo, _settings.PerformanceProfile);
        _settings.RequireBackupBeforeSensitiveAction = OnboardingBackupToggle.IsOn;
        _settings.DeepCleanByDefault = OnboardingDeepCleanToggle.IsOn;
        _settings.SkipOnboarding = OnboardingSkipToggle.IsOn;
        await _settingsStore.SaveAsync(_settings);
        ApplySettingsToUi();
    }

    private async Task SaveSettingsFromUiAsync()
    {
        _settings.RequireBackupBeforeSensitiveAction = RequireBackupToggle.IsOn;
        _settings.DeepCleanByDefault = DeepCleanDefaultToggle.IsOn;
        _settings.SkipOnboarding = SkipOnboardingToggle.IsOn;
        _settings.PerformanceProfile = GetComboText(SettingsProfileCombo, _settings.PerformanceProfile);
        _settings.NeverKillProcesses = AppSettingsStore.ParseProcessList(NeverKillTextBox.Text);
        _settings.SuggestedKillProcesses = AppSettingsStore.ParseProcessList(SuggestedKillTextBox.Text);
        await _settingsStore.SaveAsync(_settings);
        ApplySettingsToUi();
    }

    private void ShowPage(string page)
    {
        var isOptimize = string.Equals(page, "Optimize", StringComparison.OrdinalIgnoreCase);
        var isHistory = string.Equals(page, "History", StringComparison.OrdinalIgnoreCase);
        var isSpecs = string.Equals(page, "Specs", StringComparison.OrdinalIgnoreCase);
        var isSettings = string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase);

        OptimizePage.Visibility = isOptimize ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = isHistory ? Visibility.Visible : Visibility.Collapsed;
        SpecsPage.Visibility = isSpecs ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = page switch
        {
            "History" => "Hist\u00F3rico",
            "Specs" => "Specs do computador",
            "Settings" => "Configura\u00E7\u00F5es",
            _ => "Otimizar"
        };
        PageSubtitle.Text = page switch
        {
            "History" => "Gerencie snapshots locais e restaure configuracoes com rollback.",
            "Specs" => "Veja RAM, CPU, plano de energia, permissao e perfil competitivo.",
            "Settings" => "Ajuste o fluxo seguro, backup e comportamento de UAC.",
            _ => "Prepare o PC, revise o backup e aplique ajustes reversiveis."
        };

        SetNavState(NavOptimizeButton, isOptimize);
        SetNavState(NavHistoryButton, isHistory);
        SetNavState(NavSpecsButton, isSpecs);
        SetNavState(NavSettingsButton, isSettings);
    }

    private void SetNavState(Button button, bool active)
    {
        button.Background = Brush(active ? Color.FromArgb(40, 56, 189, 248) : Colors.Transparent);
        button.BorderBrush = Brush(active ? Color.FromArgb(100, 56, 189, 248) : Colors.Transparent);
    }

    private async void OnBoostAllClick(object sender, RoutedEventArgs e)
    {
        if (!await AskBackupBeforeApplyAsync("Boost agora", "Executar limpeza, otimizar RAM e ativar Modo FPS?"))
        {
            return;
        }

        await RunWithBusyAsync(BoostAllButton, async () =>
        {
            SetPanel("Boost competitivo", "Limpeza, RAM e Modo FPS em uma unica elevacao", "Aplicando");
            BoostTimeline.Visibility = Visibility.Visible;
            BoostSummary.Visibility = Visibility.Visible;
            BoostMetricsPanel.Visibility = Visibility.Collapsed;
            BoostSummary.Text = "Executando boost completo...";
            StepClean.Text = StepRam.Text = StepFps.Text = "Aguardando";

            StepClean.Text = CleanDeepToggle.IsOn ? "Limpeza profunda em andamento" : "Limpeza segura em andamento";
            StepRam.Text = "Revisando working set";
            StepFps.Text = "Aplicando ajustes reversiveis";

            var result = await _admin.RunAsync<BoostAllResult>("boost-all", new BoostAllOptions { Clean = BuildCleanOptions() });
            StepClean.Text = $"{result.Clean.FreedMB:0.#} MB em {result.Clean.FilesRemoved} arquivo(s)";
            StepRam.Text = $"{result.Memory.FreedMB} MB liberados";
            StepFps.Text = $"{result.Fps.Applied.Count} ajuste(s)";

            BoostSummary.Foreground = Brush(result.Warnings.Count > 0 ? Colors.Goldenrod : Colors.LightGreen);
            BoostSummary.Text = result.Warnings.Count > 0
                ? $"Boost concluido com avisos: {string.Join(", ", result.Warnings)}."
                : "Boost concluido com uma unica elevacao.";
            BoostMetricsPanel.Visibility = Visibility.Visible;
            BoostMetricsText.Text = FormatBoostMetrics(result);

            ReplaceLog(new[]
            {
                ("ok", "Limpeza", $"{result.Clean.FreedMB:0.#} MB em {result.Clean.FilesRemoved} arquivo(s)"),
                ("ok", "RAM", $"{FormatMemory(result.Memory.BeforeUsedMB)} -> {FormatMemory(result.Memory.AfterUsedMB)}"),
                ("ok", "Modo FPS", $"{result.Fps.Applied.Count} ajuste(s)"),
                ("info", "Energia", $"{result.Before.PowerPlan} -> {result.After.PowerPlan}")
            }.Concat(result.Warnings.Select(item => ("warn", "Aviso", item))));

            await LoadSystemInfoAsync();
            await LoadStateHistoryAsync();
        });
    }

    private async void OnCleanClick(object sender, RoutedEventArgs e)
    {
        await RunCleanCoreAsync(showConfirmation: true);
    }

    private async void OnRamClick(object sender, RoutedEventArgs e)
    {
        await RunRamCoreAsync();
    }

    private async void OnAnalyzeProcessesClick(object sender, RoutedEventArgs e)
    {
        await AnalyzeProcessesAsync();
    }

    private async void OnKillSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _processItems
            .Where(item => item.IsSelected && !item.IsLocked)
            .Select(item => new KillProcessItem { Name = item.Name, Pids = item.Pids })
            .ToList();

        if (selected.Count == 0)
        {
            ProcResult.Text = "Selecione ao menos um processo.";
            ProcResult.Foreground = Brush(Colors.IndianRed);
            return;
        }

        if (!await ConfirmAsync("Encerrar processos", $"Encerrar {selected.Count} grupo(s) de processo por PID?"))
        {
            return;
        }

        await RunWithBusyAsync(KillSelectedButton, async () =>
        {
            SetPanel("Processos", "Selecao, protecoes e encerramento por PID", "Aplicando");
            AddLog("run", "Encerramento", "aplicando por PID");
            var result = await _admin.RunAsync<KillProcessesResult>("kill-processes", selected);

            ProcResult.Text = $"{result.Killed.Count} processo(s) encerrado(s) por PID" +
                              (result.Failed.Count > 0 ? $", {result.Failed.Count} falha(s)." : ".");
            ProcResult.Foreground = Brush(result.Killed.Count > 0 ? Colors.LightGreen : Colors.IndianRed);
            AddLog(result.Killed.Count > 0 ? "ok" : "warn", "Encerrados", result.Killed.Count > 0 ? string.Join(", ", result.Killed.Take(4).Select(item => $"{item.Name} ({item.Pid})")) : "nenhum processo");
            if (result.Failed.Count > 0)
            {
                AddLog("warn", "Falhas", $"{result.Failed.Count} item(ns)");
            }

            await AnalyzeProcessesAsync();
        });
    }

    private async void OnFpsClick(object sender, RoutedEventArgs e)
    {
        await RunFpsCoreAsync(showConfirmation: true);
    }

    private async void OnRestoreModeClick(object sender, RoutedEventArgs e)
    {
        if (!await AskBackupBeforeApplyAsync("Restaurar modo", "Restaurar o snapshot anterior do Modo FPS ou aplicar fallback seguro?"))
        {
            return;
        }

        await RunWithBusyAsync(FpsButton, async () =>
        {
            SetPanel("Modo FPS", "Energia, Game Mode, Game DVR e rollback", "Restaurando");
            AddLog("run", "Restore", "aplicando estado anterior");
            var result = await _admin.RunAsync<RestoreModeResult>("fps-restore");
            FpsResult.Text = result.StateUsed
                ? "Estado anterior restaurado pelo snapshot."
                : "Configuracoes padrao restauradas por fallback.";
            FpsResult.Foreground = Brush(Colors.LightGreen);
            ReplaceLog(result.Restored.Select(item => ("ok", result.StateUsed ? "Restaurado" : "Fallback", item)));
            await LoadSystemInfoAsync();
            await LoadStateHistoryAsync();
        });
    }

    private async void OnRefreshHistoryClick(object sender, RoutedEventArgs e)
    {
        await LoadStateHistoryAsync();
    }

    private async void OnSaveStateClick(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync((Button)sender, async () =>
        {
            SetPanel("Historico local", "Estados salvos neste PC", "Salvando");
            AddLog("run", "Snapshot", "salvando estado atual");
            var result = await _history.CaptureAndSaveAsync();
            RenderHistory(result);
            ReplaceLog(new[]
            {
                ("ok", "Salvo", result.Item?.Label ?? "Estado manual"),
                ("info", "Plano", result.Item?.PowerPlanName ?? "desconhecido"),
                ("info", "Arquivo", result.Path)
            });
        });
    }

    private async void OnRestoreStateClick(object sender, RoutedEventArgs e)
    {
        if (StateHistoryList.SelectedItem is not StateSnapshotView selected)
        {
            ShowStatus("Historico local", "Selecione um estado salvo para restaurar.", InfoBarSeverity.Warning);
            return;
        }

        if (!await AskBackupBeforeApplyAsync("Restaurar estado", $"Restaurar \"{selected.Label}\"?"))
        {
            return;
        }

        await RunWithBusyAsync(RestoreStateButton, async () =>
        {
            var result = await _admin.RunAsync<StateHistoryResult>("state-restore", new { selected.Id });
            RenderHistory(result);
            ReplaceLog(result.Restored.Select(item => ("ok", "Aplicado", item)));
            await LoadSystemInfoAsync();
        });
    }

    private async void OnDeleteStateClick(object sender, RoutedEventArgs e)
    {
        if (StateHistoryList.SelectedItem is not StateSnapshotView selected)
        {
            ShowStatus("Historico local", "Selecione um estado salvo para apagar.", InfoBarSeverity.Warning);
            return;
        }

        if (!await ConfirmAsync("Apagar estado", $"Apagar \"{selected.Label}\" do historico local?"))
        {
            return;
        }

        await RunWithBusyAsync(DeleteStateButton, async () =>
        {
            var result = await _history.DeleteStateAsync(selected.Id);
            RenderHistory(result);
            ReplaceLog(new[] { (result.Removed ? "ok" : "warn", "Historico", result.Removed ? "estado apagado" : "estado nao encontrado") });
        });
    }

    private void OnStateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderSelectedStateDetails();
    }

    private async void OnCompareStateClick(object sender, RoutedEventArgs e)
    {
        if (StateHistoryList.SelectedItem is not StateSnapshotView selected)
        {
            ShowStatus("Historico local", "Selecione um estado salvo para comparar.", InfoBarSeverity.Warning);
            return;
        }

        var current = await _history.CaptureCurrentAsync("Estado atual", "current");
        SelectedSnapshotDetails.Text = FormatSnapshotComparison(selected.Snapshot, current);
    }

    private void OnExportStateClick(object sender, RoutedEventArgs e)
    {
        if (StateHistoryList.SelectedItem is not StateSnapshotView selected)
        {
            ShowStatus("Historico local", "Selecione um estado salvo para copiar.", InfoBarSeverity.Warning);
            return;
        }

        CopyTextToClipboard(StateHistoryStore.SerializeSnapshot(selected.Snapshot));
        ShowStatus("Snapshot copiado", "JSON do backup copiado para a area de transferencia.", InfoBarSeverity.Success);
    }

    private async void OnImportStateClick(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 180,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Cole aqui o JSON de um snapshot exportado"
        };

        var dialog = new ContentDialog
        {
            Title = "Importar snapshot",
            Content = input,
            PrimaryButtonText = "Importar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var snapshot = StateHistoryStore.DeserializeSnapshot(input.Text)
                ?? throw new JsonException("Snapshot vazio ou invalido.");
            RenderHistory(await _history.ImportSnapshotAsync(snapshot));
            ShowStatus("Snapshot importado", "Backup adicionado ao historico local.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("Importacao falhou", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnCs2Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync(Cs2Button, async () =>
        {
            SetPanel("Perfil CS2", "GPU, plano de energia e recursos do Windows", "Analisando");
            AddLog("run", "Perfil", "identificando configuracoes competitivas");
            var profile = await _cs2.AnalyzeAsync();
            _lastCs2Profile = profile;
            Cs2Summary.Visibility = Visibility.Visible;
            Cs2PathsText.Visibility = Visibility.Visible;
            Cs2BenchmarkPanel.Visibility = Visibility.Visible;
            Cs2Results.Visibility = Visibility.Visible;
            Cs2Summary.Text = $"GPU: {profile.GpuName} | Driver: {profile.DriverVersion} | CS2: {(profile.Cs2Detected ? "detectado" : "nao encontrado")} | Energia: {profile.PowerPlan} | Game DVR: {profile.GameDvr} | HAGS: {profile.Hags}";
            Cs2PathsText.Text = $"Steam: {profile.SteamPath ?? "nao detectado"}\nCS2: {profile.Cs2Path ?? "nao detectado"}\nLaunch options: {profile.LaunchOptions}";
            Cs2BenchmarkText.Text = FormatCs2Benchmark(profile.Benchmark);
            Cs2BenchmarkScenarios.ItemsSource = profile.Benchmark.Scenarios;
            Cs2Results.ItemsSource = profile.Recommendations;
            ReplaceLog(new[]
            {
                (profile.Cs2Detected ? "ok" : "info", "CS2", profile.Cs2Detected ? "instalacao detectada" : "nao localizado automaticamente"),
                ("info", "GPU", profile.GpuName),
                ("info", "FPS estimado", $"{profile.Benchmark.CurrentAverageFps} medio / {profile.Benchmark.CurrentOnePercentLowFps} 1% low"),
                ("info", "Energia", profile.PowerPlan),
                ("info", "Recomendacoes", $"{profile.Recommendations.Count} item(ns)")
            });
        });
    }

    private async Task<CleanTempResult> RunCleanCoreAsync(bool showConfirmation, bool rethrow = false)
    {
        var options = BuildCleanOptions();
        var modeText = options.DeepClean
            ? "Remover temporarios, Prefetch e limpar Lixeira?"
            : "Remover arquivos temporarios seguros? Prefetch e Lixeira serao preservados.";

        if (showConfirmation && !await ConfirmAsync("Limpeza de temporarios", modeText))
        {
            return new CleanTempResult();
        }

        return await RunWithBusyAsync(CleanButton, async () =>
        {
            SetPanel("Limpeza aplicada", options.DeepClean ? "Modo profundo com Prefetch e Lixeira" : "Arquivos removidos e itens preservados", "Aplicando");
            CleanResult.Text = options.DeepClean ? "Limpando em modo profundo..." : "Limpando temporarios seguros...";
            CleanResult.Foreground = Brush(Colors.LightGray);
            AddLog("run", "Varredura", "em andamento");

            var result = await _admin.RunAsync<CleanTempResult>("clean-temp", options);
            CleanResult.Text = options.DeepClean
                ? $"{result.FreedMB:0.#} MB liberados em {result.FilesRemoved} arquivos. Prefetch/Lixeira incluidos."
                : $"{result.FreedMB:0.#} MB liberados em {result.FilesRemoved} arquivos. Prefetch e Lixeira preservados.";
            CleanResult.Foreground = Brush(Colors.LightGreen);

            ReplaceLog(new[]
            {
                ("ok", "Arquivos removidos", $"{result.FilesRemoved} item(ns)"),
                ("ok", "Espaco liberado", $"{result.FreedMB:0.#} MB"),
                ("info", "Modo", result.DeepClean ? "profundo" : "seguro")
            }.Concat(result.Skipped.Select(item => ("info", "Preservado", item))));

            await LoadSystemInfoAsync();
            return result;
        }, rethrow);
    }

    private async Task<MemoryOptimizeResult> RunRamCoreAsync(bool rethrow = false)
    {
        return await RunWithBusyAsync(RamButton, async () =>
        {
            SetPanel("RAM otimizada", "Working set e memoria fisica", "Aplicando");
            RamResult.Text = "Otimizando memoria...";
            RamResult.Foreground = Brush(Colors.LightGray);
            AddLog("run", "Working set", "em andamento");

            var result = await _memory.OptimizeAsync();
            RamResult.Text = result.FreedMB > 0
                ? $"{result.FreedMB} MB liberados ({result.ProcessesTrimmed} processos)."
                : $"RAM ja otimizada ({result.ProcessesTrimmed} processos verificados).";
            RamResult.Foreground = Brush(Colors.LightGreen);

            ReplaceLog(new[]
            {
                (result.FreedMB > 0 ? "ok" : "info", "Memoria liberada", FormatMemory(result.FreedMB)),
                ("ok", "Processos verificados", result.ProcessesTrimmed.ToString()),
                ("info", "Uso antes/depois", $"{FormatMemory(result.BeforeUsedMB)} -> {FormatMemory(result.AfterUsedMB)}")
            });

            await LoadSystemInfoAsync();
            return result;
        }, rethrow);
    }

    private async Task<FpsModeResult> RunFpsCoreAsync(bool showConfirmation, bool rethrow = false)
    {
        if (showConfirmation && !await AskBackupBeforeApplyAsync("Modo FPS", "Ativar Alto desempenho, Modo Jogo, Game DVR off e limpar cache DNS?"))
        {
            return new FpsModeResult();
        }

        return await RunWithBusyAsync(FpsButton, async () =>
        {
            SetPanel("Modo FPS", "Energia, Game Mode, Game DVR e rollback", "Aplicando");
            FpsResult.Text = "Aplicando Modo FPS...";
            FpsResult.Foreground = Brush(Colors.LightGray);
            AddLog("run", "Snapshot", "preparando rollback");

            var result = await _admin.RunAsync<FpsModeResult>("fps-enable");
            FpsResult.Text = result.Applied.Count > 0
                ? $"Modo FPS ativo ({result.Applied.Count} ajustes aplicados)."
                : "Nenhum ajuste pode ser aplicado.";
            FpsResult.Foreground = Brush(result.Applied.Count > 0 ? Colors.LightGreen : Colors.IndianRed);
            ReplaceLog(result.Applied.Select(item => (item.Contains("preservado", StringComparison.OrdinalIgnoreCase) ? "info" : "ok", item.Contains("Snapshot", StringComparison.OrdinalIgnoreCase) ? "Rollback" : "Aplicado", item)));
            await LoadSystemInfoAsync();
            await LoadStateHistoryAsync();
            return result;
        }, rethrow);
    }

    private async Task AnalyzeProcessesAsync()
    {
        await RunWithBusyAsync(AnalyzeProcessesButton, async () =>
        {
            SetPanel("Processos", "Selecao, protecoes e encerramento por PID", "Analisando");
            ProcResult.Text = "";
            ProcessList.Visibility = Visibility.Visible;
            KillSelectedButton.Visibility = Visibility.Collapsed;
            _processItems.Clear();
            AddLog("run", "Processos", "analisando consumo");

            var result = await _processes.ListAsync();
            foreach (var item in result.Processes)
            {
                var tag = ProcessTags.GetTag(item.Name, _settings.NeverKillProcesses, _settings.SuggestedKillProcesses);
                var locked = string.Equals(tag, "preservado", StringComparison.OrdinalIgnoreCase);
                _processItems.Add(new ProcessCandidateView
                {
                    Name = item.Name,
                    MemMB = item.MemMB,
                    Count = item.Count,
                    Pids = item.Pids,
                    HasWindow = item.HasWindow,
                    IsLocked = locked,
                    IsSelected = !locked && ProcessTags.IsSuggested(item.Name, _settings.SuggestedKillProcesses),
                    TagText = tag,
                    ReasonText = ProcessTags.GetReason(item.Name, _settings.NeverKillProcesses, _settings.SuggestedKillProcesses)
                });
            }

            if (_processItems.Count == 0)
            {
                ProcessList.Visibility = Visibility.Collapsed;
                ProcResult.Text = "Nenhum processo pesado encontrado. Tudo limpo.";
                ProcResult.Foreground = Brush(Colors.LightGreen);
                ReplaceLog(new[] { ("ok", "Processos", "nenhum alvo pesado") });
                return;
            }

            KillSelectedButton.Visibility = Visibility.Visible;
            var selected = _processItems.Count(item => item.IsSelected);
            ProcResult.Text = $"{_processItems.Count} processos encontrados, {selected} sugeridos.";
            ProcResult.Foreground = Brush(Colors.LightGray);
            ReplaceLog(new[]
            {
                (selected > 0 ? "ok" : "info", "Pre-selecionados", $"{selected} app(s) de baixo risco"),
                ("info", "Manual", "Discord, OBS, NVIDIA e audio ficam para revisao"),
                ("info", "Protegidos", "Steam, CS2 e processos criticos")
            });
        });
    }

    private async Task LoadSystemInfoAsync()
    {
        try
        {
            var info = await _systemInfo.GetAsync();
            _lastSystemInfo = info;
            RamValue.Text = $"{info.UsedMB / 1024d:0.0} / {info.TotalMB / 1024d:0.0} GB";
            RamBar.Value = info.UsedPct;
            CpuValue.Text = info.Cpu;
            PlanValue.Text = info.PowerPlan;
            SpecsRamValue.Text = RamValue.Text;
            SpecsRamBar.Value = info.UsedPct;
            SpecsCpuValue.Text = info.Cpu;
            SpecsPlanValue.Text = info.PowerPlan;
            RenderAdmin(info.IsAdmin);
            RenderSystemScan(info);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao carregar status do sistema.", ex);
            RamValue.Text = "indisponivel";
            SpecsRamValue.Text = "indisponivel";
            ScanRamText.Text = "leitura indisponivel";
            ReadinessBadge.Text = "Revisar";
            ReadinessBadge.Foreground = Brush(Colors.Goldenrod);
        }
    }

    private async Task LoadStateHistoryAsync()
    {
        try
        {
            RenderHistory(await _history.ListAsync());
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao carregar historico.", ex);
            ShowStatus("Historico local", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void RenderHistory(StateHistoryResult result)
    {
        _lastHistory = result;
        _stateItems.Clear();
        foreach (var item in result.History.Items.Take(25))
        {
            _stateItems.Add(new StateSnapshotView(item));
        }

        HistoryPath.Text = result.History.Items.Count > 0
            ? $"{result.History.Items.Count} estado(s) salvos neste PC"
            : "Estados salvos neste PC";
        HistoryPath.Tag = result.Path;
        SettingsBackupPath.Text = result.Path;
        SpecsBackupValue.Text = result.History.Items.Count > 0
            ? $"{result.History.Items.Count} backup(s) locais"
            : "Nenhum backup local salvo";
        RenderHistoryScan(result.History.Items.Count);
    }

    private void RenderAdmin(bool isAdmin)
    {
        AdminDot.Fill = Brush(isAdmin ? Colors.LightGreen : Colors.IndianRed);
        AdminLabel.Text = isAdmin ? "Administrador" : "Acoes sensiveis usam UAC";
        SpecsAdminValue.Text = isAdmin ? "Administrador" : "UAC sob demanda";
    }

    private void BeginOnboardingScan()
    {
        OnboardingTitle.Text = "Preparando seu perfil";
        OnboardingSubtitle.Text = "Identificando memoria, processador, energia e snapshots locais antes de aplicar ajustes.";
        OnboardingRing.IsActive = true;
        OnboardingProgress.Value = 18;
        ReadinessBadge.Text = "Analisando";
        ReadinessBadge.Foreground = Brush(Colors.LightSkyBlue);
        ScanRamText.Text = "aguardando leitura";
        ScanCpuText.Text = "identificando processador";
        ScanPowerText.Text = "lendo plano ativo";
        ScanHistoryText.Text = "procurando backups";
    }

    private void RenderSystemScan(SystemInfoResult info)
    {
        OnboardingProgress.Value = Math.Max(OnboardingProgress.Value, 72);
        OnboardingTitle.Text = "Perfil do aparelho detectado";
        OnboardingSubtitle.Text = "Memoria, CPU e energia foram lidas localmente. Revise o backup antes de aplicar ajustes.";
        ReadinessBadge.Text = "Quase pronto";
        ReadinessBadge.Foreground = Brush(Colors.LightSkyBlue);
        ScanRamText.Text = $"{info.UsedPct}% em uso de {info.TotalMB / 1024d:0.0} GB";
        ScanCpuText.Text = info.Cpu;
        ScanPowerText.Text = info.PowerPlan;
    }

    private void RenderHistoryScan(int savedStates)
    {
        OnboardingProgress.Value = 100;
        OnboardingRing.IsActive = false;
        ReadinessBadge.Text = "Pronto";
        ReadinessBadge.Foreground = Brush(Colors.LightGreen);
        ScanHistoryText.Text = savedStates > 0
            ? $"{savedStates} backup(s) locais"
            : "sem backup salvo ainda";
        BackupDefaultText.Text = savedStates > 0
            ? "Acoes sensiveis vao sugerir um novo snapshot antes de alterar configuracoes."
            : "Antes do primeiro ajuste sensivel, o app vai sugerir salvar um snapshot para rollback.";
    }

    private void SetPanel(string title, string subtitle, string state)
    {
        ActionPanelTitle.Text = title;
        ActionPanelSubtitle.Text = subtitle;
        ActionPanelState.Text = state;
        SetActionProgress(true);
    }

    private void ReplaceLog(IEnumerable<(string State, string Label, string Value)> rows)
    {
        ActionLog.Children.Clear();
        foreach (var row in rows)
        {
            AddLog(row.State, row.Label, row.Value);
        }
        ActionPanelState.Text = rows.Any(row => row.State == "warn") ? "Revisar" : "Aplicado";
        SetActionProgress(false);
    }

    private void SetActionProgress(bool isRunning)
    {
        ActionProgressBar.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        ActionProgressBar.IsIndeterminate = isRunning;
    }

    private void AddLog(string state, string label, string value)
    {
        var dotColor = state switch
        {
            "ok" => Colors.LightGreen,
            "warn" => Colors.Goldenrod,
            "run" => Colors.Goldenrod,
            "err" => Colors.IndianRed,
            _ => Colors.LightSkyBlue
        };

        var row = new Border
        {
            BorderBrush = Brush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brush(Color.FromArgb(8, 255, 255, 255)),
            Padding = new Thickness(9, 8, 9, 8)
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brush(dotColor),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 0, 0)
        };

        var text = new TextBlock
        {
            Text = $"{label}: {value}",
            Foreground = Brush(Colors.Gainsboro),
            FontSize = 12
        };

        Grid.SetColumn(text, 1);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        row.Child = grid;
        ActionLog.Children.Add(row);
    }

    private async Task<T> RunWithBusyAsync<T>(Button button, Func<Task<T>> action, bool rethrow = false)
    {
        button.IsEnabled = false;
        try
        {
            return await action();
        }
        catch (OperationCanceledException ex)
        {
            ShowStatus("Acao cancelada", ex.Message, InfoBarSeverity.Warning);
            _logger.Info(ex.Message);
            if (rethrow) throw;
            return default!;
        }
        catch (Exception ex)
        {
            ShowStatus("Falha na acao", ex.Message, InfoBarSeverity.Error);
            AddLog("err", "Erro", ex.Message);
            _logger.Error("Falha na acao da UI.", ex);
            if (rethrow) throw;
            return default!;
        }
        finally
        {
            button.IsEnabled = true;
            SetActionProgress(false);
        }
    }

    private async Task RunWithBusyAsync(Button button, Func<Task> action)
    {
        await RunWithBusyAsync(button, async () =>
        {
            await action();
            return true;
        });
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Continuar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> AskBackupBeforeApplyAsync(string title, string message)
    {
        if (!_settings.RequireBackupBeforeSensitiveAction)
        {
            return await ConfirmAsync(title, message);
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = $"{message}\n\nRecomendado: salve um backup do estado atual antes de alterar configuracoes do Windows. Assim voce pode restaurar o plano de energia, Game Mode e Game DVR depois.",
            PrimaryButtonText = "Salvar backup e continuar",
            SecondaryButtonText = "Continuar sem backup",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Primary)
        {
            return await SavePreActionBackupAsync(title);
        }

        return choice == ContentDialogResult.Secondary;
    }

    private async Task<bool> SavePreActionBackupAsync(string actionName)
    {
        try
        {
            SetPanel("Backup local", "Snapshot antes de aplicar ajustes", "Salvando");
            AddLog("run", "Backup", $"salvando estado antes de {actionName}");
            var result = await _history.CaptureAndSaveAsync($"Antes de {actionName}", "pre-action");
            RenderHistory(result);
            ReplaceLog(new[]
            {
                ("ok", "Backup salvo", result.Item?.Label ?? "Estado atual"),
                ("info", "Plano", result.Item?.PowerPlanName ?? "desconhecido"),
                ("info", "Arquivo", result.Path)
            });
            ShowStatus("Backup salvo", "Snapshot local criado. O ajuste pode continuar.", InfoBarSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao salvar backup antes da acao.", ex);
            ShowStatus("Backup nao salvo", ex.Message, InfoBarSeverity.Error);
            return await ConfirmAsync("Continuar sem backup?", "Nao foi possivel salvar o snapshot. Continuar mesmo assim?");
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private CleanTempOptions BuildCleanOptions()
    {
        return new CleanTempOptions { DeepClean = CleanDeepToggle.IsOn };
    }

    private void RenderCleanPreviewHint()
    {
        CleanPreviewText.Text = CleanDeepToggle.IsOn
            ? "Modo profundo inclui Prefetch e Lixeira. Use antes apenas quando quiser limpeza agressiva."
            : "Limpeza segura preserva Prefetch e Lixeira para evitar stutter e recarregamentos.";
    }

    private static string FormatCleanPreview(CleanTempResult preview)
    {
        var preserved = preview.Skipped.Count > 0
            ? $" Preservado: {string.Join(", ", preview.Skipped)}."
            : " Inclui Prefetch e Lixeira.";
        return $"Preview: {preview.FilesRemoved} arquivo(s), {preview.FreedMB:0.#} MB em {preview.Details.Count} alvo(s).{preserved}";
    }

    private static string FormatBoostMetrics(BoostAllResult result)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"RAM: {FormatMemory(result.Before.UsedMB)} -> {FormatMemory(result.After.UsedMB)} ({FormatMemory(result.Memory.FreedMB)} liberados)",
            $"Energia: {result.Before.PowerPlan} -> {result.After.PowerPlan}",
            $"Limpeza: {result.Clean.FreedMB:0.#} MB / {result.Clean.FilesRemoved} arquivo(s) / {(result.Clean.DeepClean ? "profunda" : "segura")}",
            $"Modo FPS: {result.Fps.Applied.Count} ajuste(s) / snapshot: {(string.IsNullOrWhiteSpace(result.Fps.StatePath) ? "indisponivel" : result.Fps.StatePath)}",
            $"Avisos: {(result.Warnings.Count == 0 ? "nenhum" : string.Join(", ", result.Warnings))}"
        });
    }

    private static string FormatCs2Benchmark(Cs2BenchmarkResult benchmark)
    {
        var factors = benchmark.Factors.Count > 0
            ? string.Join("; ", benchmark.Factors.Take(5))
            : "sem fatores detectados";

        return string.Join(Environment.NewLine, new[]
        {
            $"{benchmark.CurrentAverageFps} FPS medio / {benchmark.CurrentOnePercentLowFps} FPS 1% low agora",
            $"{benchmark.BoostAverageFps} FPS medio / {benchmark.BoostOnePercentLowFps} FPS 1% low com boost",
            $"Confianca: {benchmark.Confidence}. {benchmark.Basis}",
            $"Base: {factors}"
        });
    }

    private void RenderSelectedStateDetails()
    {
        if (StateHistoryList.SelectedItem is not StateSnapshotView selected)
        {
            SelectedSnapshotDetails.Text = "Selecione um backup para ver plano de energia, chaves do Registro e diferencas.";
            return;
        }

        SelectedSnapshotDetails.Text = FormatSnapshotDetails(selected.Snapshot);
    }

    private static string FormatSnapshotDetails(StateSnapshot snapshot)
    {
        var registry = snapshot.Registry.Count == 0
            ? "sem chaves registradas"
            : string.Join(Environment.NewLine, snapshot.Registry.Select(item =>
                $"{item.Path} / {item.Name}: {(item.Exists ? item.Value?.ToString() ?? "null" : "nao existia")}"));

        return string.Join(Environment.NewLine, new[]
        {
            $"ID: {snapshot.Id}",
            $"Criado: {snapshot.CreatedAt:dd/MM/yyyy HH:mm}",
            $"Origem: {snapshot.Source}",
            $"Plano: {snapshot.PowerPlanName} ({snapshot.PowerPlanGuid ?? "sem guid"})",
            registry
        });
    }

    private static string FormatSnapshotComparison(StateSnapshot saved, StateSnapshot current)
    {
        var rows = new List<string>
        {
            $"Snapshot: {saved.Label}",
            $"Plano salvo: {saved.PowerPlanName}",
            $"Plano atual: {current.PowerPlanName}"
        };

        foreach (var savedEntry in saved.Registry)
        {
            var currentEntry = current.Registry.FirstOrDefault(item =>
                string.Equals(item.Path, savedEntry.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Name, savedEntry.Name, StringComparison.OrdinalIgnoreCase));
            var savedValue = savedEntry.Exists ? savedEntry.Value?.ToString() ?? "null" : "nao existia";
            var currentValue = currentEntry?.Exists == true ? currentEntry.Value?.ToString() ?? "null" : "nao existe";
            rows.Add($"{savedEntry.Name}: salvo={savedValue} atual={currentValue}");
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static void CopyTextToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static void SelectComboItem(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static string GetComboText(ComboBox combo, string fallback)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
    }

    private static string FormatMemory(long mb)
    {
        return mb >= 1024 ? $"{mb / 1024d:0.0} GB" : $"{mb} MB";
    }

    private static SolidColorBrush Brush(Color color) => new(color);
}
