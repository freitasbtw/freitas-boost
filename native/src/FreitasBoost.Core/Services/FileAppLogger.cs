using System.Text;

namespace FreitasBoost.Core.Services;

public interface IAppLogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logPath;
    private readonly object _gate = new();

    public FileAppLogger(string component)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        var logDir = Path.Combine(baseDir, "Freitas Boost", "logs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, $"{component}.log");
    }

    public string LogPath => _logPath;

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(message);

        if (exception is not null)
        {
            line.Append(" :: ").Append(exception);
        }

        lock (_gate)
        {
            File.AppendAllText(_logPath, line.AppendLine().ToString(), Encoding.UTF8);
        }
    }
}

