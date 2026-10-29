using System.Globalization;

namespace PhotoCloud.Core.Logging;

public static class UploadLogReader
{
    /// <summary>Returns the newest <paramref name="count"/> entries across all daily log files, oldest first.</summary>
    public static IReadOnlyList<LogEntry> ReadLast(string logDirectory, int count)
    {
        if (!Directory.Exists(logDirectory))
        {
            return [];
        }

        var result = new List<LogEntry>();
        foreach (var file in Directory.EnumerateFiles(logDirectory, "upload-*.log").OrderDescending())
        {
            var entries = File.ReadAllLines(file).Select(Parse).OfType<LogEntry>().ToList();
            entries.AddRange(result);
            result = entries;
            if (result.Count >= count)
            {
                break;
            }
        }

        return result.Count > count ? result.GetRange(result.Count - count, count) : result;
    }

    /// <summary>Parst eine Logzeile; liefert null, wenn das Format nicht passt.</summary>
    internal static LogEntry? Parse(string line)
    {
        var parts = line.Split(" | ", 6);
        if (parts.Length != 6
            || !DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            || !Enum.TryParse<LogLevel>(parts[1], ignoreCase: true, out var level)
            || !long.TryParse(parts[4], CultureInfo.InvariantCulture, out var size))
        {
            return null;
        }

        return new LogEntry(time, level, parts[2], parts[3], size, parts[5]);
    }
}
