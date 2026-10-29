namespace PhotoCloud.Core.Storage;

public sealed record SaveResult(string Folder, string FileName, long SizeBytes);

public sealed class UploadRejectedException(string message) : Exception(message);

/// <summary>Writes files into a fixed set of existing folders below a share root. Never creates folders.</summary>
public sealed class CloudStorage
{
    private readonly string _rootPath;
    private readonly IReadOnlyList<string> _folders;
    private readonly HashSet<string> _allowedExtensions;
    private readonly long _maxBytes;

    /// <summary>Legt Wurzelpfad, erlaubte Ordner und Endungen sowie das Größenlimit (MB) fest.</summary>
    public CloudStorage(string rootPath, IEnumerable<string> folders, IEnumerable<string> allowedExtensions, int maxFileSizeMb)
    {
        _rootPath = rootPath;
        _folders = folders.ToList();
        _allowedExtensions = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
        _maxBytes = maxFileSizeMb * 1024L * 1024L;
    }

    public IReadOnlyList<string> Folders => _folders;

    /// <summary>Prüft Ordner, Name und Endung und streamt die Datei ins Ziel; bei Fehlern wird die Teildatei gelöscht.</summary>
    public async Task<SaveResult> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var configuredFolder = _folders.FirstOrDefault(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))
            ?? throw new UploadRejectedException($"Zielordner '{folder}' ist nicht konfiguriert.");

        var safeName = SanitizeFileName(fileName);
        var extension = Path.GetExtension(safeName);
        if (!_allowedExtensions.Contains(extension))
        {
            throw new UploadRejectedException($"Dateityp '{extension}' ist nicht erlaubt.");
        }

        var folderPath = Path.Combine(_rootPath, configuredFolder);
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Zielordner '{folderPath}' existiert nicht.");
        }

        var (stream, finalPath) = CreateUniqueFile(folderPath, safeName);
        long written = 0;
        try
        {
            await using (stream)
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > _maxBytes)
                    {
                        throw new UploadRejectedException($"Datei überschreitet die maximale Größe von {_maxBytes / 1024 / 1024} MB.");
                    }

                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
        }
        catch
        {
            TryDelete(finalPath);
            throw;
        }

        return new SaveResult(configuredFolder, Path.GetFileName(finalPath), written);
    }

    // CreateNew is atomic, so concurrent uploads of the same name never overwrite each other.
    /// <summary>Legt die Datei an; bei vorhandenem Namen wird ein Zähler angehängt ("name (1).ext").</summary>
    private static (FileStream Stream, string Path) CreateUniqueFile(string folderPath, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var counter = 0; ; counter++)
        {
            var candidate = counter == 0 ? fileName : $"{baseName} ({counter}){extension}";
            var path = Path.Combine(folderPath, candidate);
            try
            {
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                return (stream, path);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
    }

    /// <summary>Reduziert den Namen auf den reinen Dateinamen und lehnt ungültige Namen ab.</summary>
    private static string SanitizeFileName(string fileName)
    {
        var name = fileName.Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..].Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
        {
            throw new UploadRejectedException($"Ungültiger Dateiname '{fileName}'.");
        }

        return name;
    }

    /// <summary>Löscht eine Datei und ignoriert dabei IO-Fehler.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
