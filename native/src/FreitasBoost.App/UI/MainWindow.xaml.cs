using System.Collections.ObjectModel;
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
    private readonly Cs2ProfileAnalyzer _cs2 = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private bool _startupOnboardingShown;

    private readonly ObservableCollection<ProcessCandidateView> _processItems = [];
    private readonly ObservableCollection<StateSnapshotView> _stateItems = [];

    public MainWindow()
    {
        InitializeComponent();

        _admin = new AdminActionClient(_logger);
        _memory = new MemoryOptimizer(_logger);
        _processes = new ProcessManager(_logger);
        _history = new StateHistoryStore(_logger);

        ExtendsContentIntoTitleBar = true;
        ProcessList.ItemsSource = _processItems;
        StateHistoryList.ItemsSource = _stateItems;
        BeginOnboardingScan();
        SetActionProgress(false);
        ShowPage("Otimizar");
        AddLog("info", "Status", "Aguardando uma otimizacao");

        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += async (_, _) => await LoadSystemInfoAsync();

        Activated += async (_, _) =>
        {
            await LoadSystemInfoAsync();
            await LoadStateHistoryAsync();
            _refreshTimer.Start();
            if (!_startupOnboardingShown)
            {
                _startupOnboardingShown = true;
                await Task.Delay(250);
                await ShowStartupOnboardingAsync();
            }
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

    private async void OnShowOnboardingClick(object sender, RoutedEventArgs e)
    {
        await ShowStartupOnboardingAsync();
    }

    private void ShowPage(string page)
    {
        var isOptimize = string.Equals(page, "Otimizar", StringComparison.OrdinalIgnoreCase);
        var isHistory = string.Equals(page, "Histórico", StringComparison.OrdinalIgnoreCase);
        var isSpecs = string.Equals(page, "Specs do computador", StringComparison.OrdinalIgnoreCase);
        var isSettings = string.Equals(page, "Configurações", StringComparison.OrdinalIgnoreCase);

        OptimizePage.Visibility = isOptimize ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = isHistory ? Visibility.Visible : Visibility.Collapsed;
        SpecsPage.Visibility = isSpecs ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = page;
        PageSubtitle.Text = page switch
        {
            "Histórico" => "Gerencie snapshots locais e restaure configuracoes com rollback.",
            "Specs do computador" => "Veja RAM, CPU, plano de energia, permissao e perfil competitivo.",
            "Configurações" => "Ajuste o fluxo seguro, backup e comportamento de UAC.",
            _ => "Prepare o PC, revise o backup e aplique ajustes reversiveis."
        };

        SetNavState(NavOptimizeButton, isOptimize);
        SetNavState(NavHistoryButton, isHistory);
        SetNavState(NavSpecsButton, isSpecs);
        SetNavState(NavSettingsButton, isSettings);
    }

    private void SetNavState(Button button, bool active)
    {
        button.Background = Brush(active ? Color.FromArgb(40, 52, 211, 153) : Colors.Transparent);
        button.BorderBrush = Brush(active ? Color.FromArgb(100, 52, 211, 153) : Colors.Transparent);
    }

    private async Task ShowStartupOnboardingAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Bem-vindo ao Freitas Boost",
            Content = BuildOnboardingDialogContent(),
            PrimaryButtonText = "Comecar diagnostico",
            SecondaryButtonText = "Ver specs",
            CloseButtonText = "Agora nao",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            ShowPage("Specs do computador");
            return;
        }

        ShowPage("Otimizar");
    }

    private static StackPanel BuildOnboardingDialogContent()
    {
        return new StackPanel
        {
            Spacing = 12,
            MaxWidth = 560,
            Children =
            {
                new TextBlock
                {
                    Text = "Ao entrar, o app identifica as configuracoes do aparelho, mostra o estado atual e prepara um fluxo com backup antes de qualquer ajuste sensivel.",
                    Foreground = Brush(Colors.Gainsboro),
                    TextWrapping = TextWrapping.Wrap
                },
                BuildOnboardingStep("1", "Identificar o PC", "RAM, CPU, plano de energia, permissao e historico local sao lidos automaticamente."),
                BuildOnboardingStep("2", "Salvar rollback", "Antes de aplicar Boost, Modo FPS ou restauracoes, o backup aparece como opcao principal."),
                BuildOnboardingStep("3", "Acompanhar execucao", "Cada etapa aparece no painel de aplicacao com progresso, resultado e erros visiveis.")
            }
        };
    }

    private static Border BuildOnboardingStep(string marker, string title, string description)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Brush(Color.FromArgb(34, 52, 211, 153)),
            BorderBrush = Brush(Color.FromArgb(85, 52, 211, 153)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = marker,
                Foreground = Brush(Colors.LightGreen),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(Colors.White),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush(Colors.DarkGray),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        Grid.SetColumn(text, 1);
        grid.Children.Add(badge);
        grid.Children.Add(text);

        return new Border
        {
            Background = Brush(Color.FromArgb(12, 255, 255, 255)),
            BorderBrush = Brush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = grid
        };
    }

    private async void OnBoostAllClick(object sender, RoutedEventArgs e)
    {
        if (!await AskBackupBeforeApplyAsync("Boost agora", "Executar limpeza, otimizar RAM e ativar Modo FPS?"))
        {
            return;
        }

        await RunWithBusyAsync(BoostAllButton, async () =>
        {
            BoostTimeline.Visibility = Visibility.Visible;
            BoostSummary.Visibility = Visibility.Visible;
            BoostSummary.Text = "Executando boost completo...";
            StepClean.Text = StepRam.Text = StepFps.Text = "Aguardando";

            var totalFreed = 0d;
            var fpsApplied = 0;
            var errors = new List<string>();

            try
            {
                StepClean.Text = "Limpando temporarios seguros";
                var clean = await RunCleanCoreAsync(showConfirmation: false, rethrow: true);
                totalFreed += clean.FreedMB;
                StepClean.Text = $"{clean.FreedMB:0.#} MB liberados";
            }
            catch
            {
                errors.Add("limpeza");
                StepClean.Text = "Falha na limpeza";
            }

            try
            {
                StepRam.Text = "Revisando working set";
                var ram = await RunRamCoreAsync(rethrow: true);
                totalFreed += ram.FreedMB;
                StepRam.Text = $"{ram.FreedMB} MB liberados";
            }
            catch
            {
                errors.Add("RAM");
                StepRam.Text = "Falha na RAM";
            }

            try
            {
                StepFps.Text = "Aplicando ajustes reversiveis";
                var fps = await RunFpsCoreAsync(showConfirmation: false, rethrow: true);
                fpsApplied = fps.Applied.Count;
                StepFps.Text = $"{fpsApplied} ajuste(s)";
            }
            catch
            {
                errors.Add("Modo FPS");
                StepFps.Text = "Falha no Modo FPS";
            }

            BoostSummary.Foreground = Brush(errors.Count > 0 ? Colors.IndianRed : Colors.LightGreen);
            BoostSummary.Text = errors.Count > 0
                ? $"Boost concluido com avisos em: {string.Join(", ", errors)}."
                : $"Boost concluido. {totalFreed:0.#} MB liberados e {fpsApplied} ajustes reversiveis aplicados.";
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
            .Where(item => item.IsSelected)
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

    private async void OnCs2Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync(Cs2Button, async () =>
        {
            SetPanel("Perfil CS2", "GPU, plano de energia e recursos do Windows", "Analisando");
            AddLog("run", "Perfil", "identificando configuracoes competitivas");
            var profile = await _cs2.AnalyzeAsync();
            Cs2Summary.Visibility = Visibility.Visible;
            Cs2Results.Visibility = Visibility.Visible;
            Cs2Summary.Text = $"GPU: {profile.GpuName} | CS2: {(profile.Cs2Detected ? "detectado" : "nao encontrado")} | Energia: {profile.PowerPlan} | Game DVR: {profile.GameDvr} | HAGS: {profile.Hags}";
            Cs2Results.ItemsSource = profile.Recommendations;
            ReplaceLog(new[]
            {
                (profile.Cs2Detected ? "ok" : "info", "CS2", profile.Cs2Detected ? "instalacao detectada" : "nao localizado automaticamente"),
                ("info", "GPU", profile.GpuName),
                ("info", "Energia", profile.PowerPlan),
                ("info", "Recomendacoes", $"{profile.Recommendations.Count} item(ns)")
            });
        });
    }

    private async Task<CleanTempResult> RunCleanCoreAsync(bool showConfirmation, bool rethrow = false)
    {
        if (showConfirmation && !await ConfirmAsync("Limpeza de temporarios", "Remover arquivos temporarios seguros? Prefetch e Lixeira serao preservados."))
        {
            return new CleanTempResult();
        }

        return await RunWithBusyAsync(CleanButton, async () =>
        {
            SetPanel("Limpeza aplicada", "Arquivos removidos e itens preservados", "Aplicando");
            CleanResult.Text = "Limpando temporarios seguros...";
            CleanResult.Foreground = Brush(Colors.LightGray);
            AddLog("run", "Varredura", "em andamento");

            var result = await _admin.RunAsync<CleanTempResult>("clean-temp", new CleanTempOptions());
            CleanResult.Text = $"{result.FreedMB:0.#} MB liberados em {result.FilesRemoved} arquivos. Prefetch e Lixeira preservados.";
            CleanResult.Foreground = Brush(Colors.LightGreen);

            ReplaceLog(new[]
            {
                ("ok", "Arquivos removidos", $"{result.FilesRemoved} item(ns)"),
                ("ok", "Espaco liberado", $"{result.FreedMB:0.#} MB"),
                ("info", "Tradeoff", "limpeza conservadora")
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
                _processItems.Add(new ProcessCandidateView
                {
                    Name = item.Name,
                    MemMB = item.MemMB,
                    Count = item.Count,
                    Pids = item.Pids,
                    HasWindow = item.HasWindow,
                    IsSelected = ProcessTags.IsSuggested(item.Name)
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
        _stateItems.Clear();
        foreach (var item in result.History.Items.Take(4))
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

    private static string FormatMemory(long mb)
    {
        return mb >= 1024 ? $"{mb / 1024d:0.0} GB" : $"{mb} MB";
    }

    private static SolidColorBrush Brush(Color color) => new(color);
}
