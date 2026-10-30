using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoCloud.Core.Configuration;

public sealed class PhotoCloudConfig
{
    public const string FileName = "photocloud.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [JsonPropertyName("Cloud-Settings")]
    public CloudSettings CloudSettings { get; set; } = new();

    public ServerSettings Server { get; set; } = new();

    [JsonPropertyName("Cloud-Storage")]
    public CloudStorageSettings CloudStorage { get; set; } = new();

    /// <summary>Liest die JSON-Konfiguration (Kommentare und nachgestellte Kommas erlaubt).</summary>
    public static PhotoCloudConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PhotoCloudConfig>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Konfiguration '{path}' ist leer.");
    }

    /// <summary>Schreibt die Konfiguration erst in eine temporäre Datei und ersetzt danach die Zieldatei.</summary>
    public static void Save(string path, PhotoCloudConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, config, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Prüft die für den laufenden Upload-Server relevanten Einstellungen.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(CloudSettings.Host)
            || CloudSettings.Host.Contains('\\')
            || CloudSettings.Host.Contains('/'))
        {
            errors.Add("Cloud-Settings.Host muss ein Rechnername oder eine IP-Adresse ohne Pfad sein.");
        }

        if (string.IsNullOrWhiteSpace(CloudSettings.Share)
            || CloudSettings.Share is "." or ".."
            || CloudSettings.Share.Contains('\\')
            || CloudSettings.Share.Contains('/')
            || CloudSettings.Share.Contains(':'))
        {
            errors.Add("Cloud-Settings.Share muss ein einzelner Freigabename sein.");
        }

        if (Server.Port is < 1 or > 65535)
        {
            errors.Add("Server.Port muss zwischen 1 und 65535 liegen.");
        }

        if (Server.AllowedClientIps.Count == 0)
        {
            errors.Add("Mindestens eine erlaubte IPv4-Adresse muss eingetragen sein.");
        }
        else
        {
            foreach (var ip in Server.AllowedClientIps)
            {
                if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                {
                    errors.Add($"'{ip}' ist keine gültige IPv4-Adresse für Server.AllowedClientIps.");
                }
            }
        }

        if (CloudStorage.TargetFolders.Count == 0)
        {
            errors.Add("Mindestens ein Cloud-Storage.TargetFolders-Eintrag ist erforderlich.");
        }
        else
        {
            foreach (var folder in CloudStorage.TargetFolders)
            {
                var segments = folder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (string.IsNullOrWhiteSpace(folder)
                    || Path.IsPathRooted(folder)
                    || segments.Any(segment => segment is "." or ".."))
                {
                    errors.Add($"'{folder}' muss ein sicherer, relativer Zielordner innerhalb der Freigabe sein.");
                }
            }
        }

        if (CloudStorage.MaxFileSizeMb <= 0)
        {
            errors.Add("Cloud-Storage.MaxFileSizeMb muss größer als 0 sein.");
        }

        if (CloudStorage.AllowedExtensions.Count == 0)
        {
            errors.Add("Mindestens eine erlaubte Dateiendung muss eingetragen sein.");
        }
        else
        {
            foreach (var extension in CloudStorage.AllowedExtensions)
            {
                if (extension.Length < 2
                    || extension[0] != '.'
                    || extension.Skip(1).Any(character => !char.IsLetterOrDigit(character)))
                {
                    errors.Add($"'{extension}' ist keine gültige Dateiendung wie '.jpg'.");
                }
            }
        }

        return errors;
    }

    /// <summary>PHOTOCLOUD_CONFIG, otherwise the nearest photocloud.config.json above the app or working directory.</summary>
    public static string? Locate()
    {
        var env = Environment.GetEnvironmentVariable("PHOTOCLOUD_CONFIG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return File.Exists(env) ? env : null;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

public sealed class CloudSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 445;
    public string Share { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    [JsonIgnore]
    public string SharePath => $@"\\{Host}\{Share}";
}

public sealed class ServerSettings
{
    public int Port { get; set; } = 5000;
    public List<string> AllowedClientIps { get; set; } = new();
}

public sealed class CloudStorageSettings
{
    public List<string> TargetFolders { get; set; } = new();
    public int MaxFileSizeMb { get; set; } = 100;
    public List<string> AllowedExtensions { get; set; } = new() { ".jpg", ".jpeg", ".png", ".heic", ".webp" };
}
