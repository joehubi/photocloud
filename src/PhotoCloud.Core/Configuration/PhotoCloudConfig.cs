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
    };

    [JsonPropertyName("Cloud-Settings")]
    public CloudSettings CloudSettings { get; set; } = new();

    public ServerSettings Server { get; set; } = new();

    [JsonPropertyName("Cloud-Storage")]
    public CloudStorageSettings CloudStorage { get; set; } = new();

    public UnittestSettings Unittest { get; set; } = new();

    public static PhotoCloudConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PhotoCloudConfig>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Konfiguration '{path}' ist leer.");
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

public sealed class UnittestSettings
{
    public string TargetFolder { get; set; } = "";
}
