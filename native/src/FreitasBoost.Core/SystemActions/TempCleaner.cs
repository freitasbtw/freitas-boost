using FreitasBoost.Core.Models;
using FreitasBoost.Core.Services;

namespace FreitasBoost.Core.SystemActions;

public sealed class TempCleaner
{
    private readonly IAppLogger _logger;

    public TempCleaner(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<CleanTempResult> CleanAsync(CleanTempOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new CleanTempOptions();
        var skipped = new List<string>();
        var targets = new List<string?>
        {
            Environment.GetEnvironmentVariable("TEMP"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\INetCache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => path!)
        .ToList();

        if (options.DeepClean)
        {
            targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"));
        }
        else
        {
            skipped.Add("Prefetch preservado para evitar piorar carregamentos");
        }

        long freed = 0;
        var removed = 0;
        var details = new List<CleanTargetDetail>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(target))
            {
                continue;
            }

            var before = freed;
            foreach (var file in EnumerateFilesSafe(target))
            {
                cancellationToken.ThrowIfCancellationRequested();
                long length = 0;
                try { length = new FileInfo(file).Length; } catch { }

                try
                {
                    File.Delete(file);
                    freed += length;
                    removed++;
                }
                catch (Exception ex)
                {
                    _logger.Info($"Arquivo preservado durante limpeza: {file} ({ex.GetType().Name})");
                }
            }

            foreach (var dir in EnumerateDirectoriesSafe(target).OrderByDescending(static path => path.Length))
            {
                try { Directory.Delete(dir, recursive: false); } catch { }
            }

            details.Add(new CleanTargetDetail
            {
                Path = target,
                FreedMB = Math.Round((freed - before) / 1024d / 1024d, 1)
            });
        }

        if (options.DeepClean)
        {
            try
            {
                _ = NativeCommand.RunAsync("powershell.exe", "-NoProfile -NonInteractive -Command Clear-RecycleBin -Force", cancellationToken: cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _logger.Info($"Lixeira nao foi limpa: {ex.Message}");
            }
        }
        else
        {
            skipped.Add("Lixeira preservada");
        }

        var result = new CleanTempResult
        {
            DeepClean = options.DeepClean,
            FreedBytes = freed,
            FreedMB = Math.Round(freed / 1024d / 1024d, 1),
            FilesRemoved = removed,
            RecycleBin = options.DeepClean,
            Skipped = skipped,
            Details = details
        };

        _logger.Info($"Limpeza concluida: {result.FreedMB} MB, {result.FilesRemoved} arquivo(s).");
        return Task.FromResult(result);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files = [];
            string[] dirs = [];

            try { files = Directory.GetFiles(current); } catch { }
            try { dirs = Directory.GetDirectories(current); } catch { }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var dir in dirs)
            {
                pending.Push(dir);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root)
    {
        var pending = new Stack<string>();
        var all = new List<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] dirs = [];
            try { dirs = Directory.GetDirectories(current); } catch { }

            foreach (var dir in dirs)
            {
                all.Add(dir);
                pending.Push(dir);
            }
        }

        return all;
    }
}

