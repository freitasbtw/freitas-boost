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
        AddLog("info", "Status", "Aguardando uma otimizacao");

        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += async (_, _) => await LoadSystemInfoAsync();

        Activated += async (_, _) =>
        {
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

    private async void OnBoostAllClick(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Boost agora", "Executar limpeza, otimizar RAM e ativar Modo FPS?"))
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
        if (!await ConfirmAsync("Restaurar modo", "Restaurar o snapshot anterior do Modo FPS ou aplicar fallback seguro?"))
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

        if (!await ConfirmAsync("Restaurar estado", $"Restaurar \"{selected.Label}\"?"))
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
            var profile = await _cs2.AnalyzeAsync();
            Cs2Summary.Visibility = Visibility.Visible;
            Cs2Results.Visibility = Visibility.Visible;
            Cs2Summary.Text = $"GPU: {profile.GpuName} | CS2: {(profile.Cs2Detected ? "detectado" : "nao encontrado")} | Energia: {profile.PowerPlan} | Game DVR: {profile.GameDvr} | HAGS: {profile.Hags}";
            Cs2Results.ItemsSource = profile.Recommendations;
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
        if (showConfirmation && !await ConfirmAsync("Modo FPS", "Ativar Alto desempenho, Modo Jogo, Game DVR off e limpar cache DNS?"))
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
            RenderAdmin(info.IsAdmin);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao carregar status do sistema.", ex);
            RamValue.Text = "indisponivel";
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
    }

    private void RenderAdmin(bool isAdmin)
    {
        AdminDot.Fill = Brush(isAdmin ? Colors.LightGreen : Colors.IndianRed);
        AdminLabel.Text = isAdmin ? "Administrador" : "Acoes sensiveis usam UAC";
    }

    private void SetPanel(string title, string subtitle, string state)
    {
        ActionPanelTitle.Text = title;
        ActionPanelSubtitle.Text = subtitle;
        ActionPanelState.Text = state;
    }

    private void ReplaceLog(IEnumerable<(string State, string Label, string Value)> rows)
    {
        ActionLog.Children.Clear();
        foreach (var row in rows)
        {
            AddLog(row.State, row.Label, row.Value);
        }
        ActionPanelState.Text = rows.Any(row => row.State == "warn") ? "Revisar" : "Aplicado";
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
