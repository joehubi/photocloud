namespace PhotoCloud.Core.Logging;

public enum LogLevel
{
    Info,
    Error,
}

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Folder, string FileName, long SizeBytes, string Message);

/// <summary>Appends entries to logs\upload-yyyy-MM-dd.log (one file per day, derived from the entry timestamp).</summary>
public sealed class UploadLogWriter(string logDirectory)
{
    private readonly object _lock = new();

    public string LogDirectory { get; } = logDirectory;

    public void Write(LogEntry entry)
    {
        var path = Path.Combine(LogDirectory, $"upload-{entry.Timestamp:yyyy-MM-dd}.log");
        var line = string.Join(" | ",
            entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            entry.Level.ToString().ToUpperInvariant(),
            entry.Folder,
            entry.FileName,
            entry.SizeBytes,
            entry.Message.ReplaceLineEndings(" "));

        lock (_lock)
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
