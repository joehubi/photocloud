using PhotoCloud.Core.Configuration;

namespace PhotoCloud.Tests;

public class PhotoCloudConfigTests
{
    [Fact]
    public void Validate_AcceptsTypicalSettings()
    {
        var config = CreateValidConfig();

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void Validate_RejectsInvalidPortsIpsAndPaths()
    {
        var config = CreateValidConfig();
        config.Server.Port = 70000;
        config.Server.AllowedClientIps = ["::1"];
        config.CloudStorage.TargetFolders = [@"..\outside"];
        config.CloudStorage.AllowedExtensions = ["jpg"];
        config.CloudStorage.MaxFileSizeMb = 0;

        var errors = config.Validate();

        Assert.Contains(errors, error => error.Contains("Server.Port"));
        Assert.Contains(errors, error => error.Contains("IPv4"));
        Assert.Contains(errors, error => error.Contains("relativer Zielordner"));
        Assert.Contains(errors, error => error.Contains("Dateiendung"));
        Assert.Contains(errors, error => error.Contains("MaxFileSizeMb"));
    }

    [Fact]
    public void Save_WritesConfigThatCanBeLoadedAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "photocloud-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, PhotoCloudConfig.FileName);

        try
        {
            var config = CreateValidConfig();
            PhotoCloudConfig.Save(path, config);

            var loaded = PhotoCloudConfig.Load(path);

            Assert.Equal(config.CloudSettings.Host, loaded.CloudSettings.Host);
            Assert.Equal(config.CloudSettings.SharePath, loaded.CloudSettings.SharePath);
            Assert.Equal(config.Server.AllowedClientIps, loaded.Server.AllowedClientIps);
            Assert.Equal(config.CloudStorage.TargetFolders, loaded.CloudStorage.TargetFolders);
            Assert.DoesNotContain("SharePath", File.ReadAllText(path));
            Assert.DoesNotContain("Unittest", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PhotoCloudConfig CreateValidConfig() => new()
    {
        CloudSettings = new CloudSettings { Host = "nas", Share = "photos" },
        Server = new ServerSettings { Port = 5000, AllowedClientIps = ["127.0.0.1"] },
        CloudStorage = new CloudStorageSettings
        {
            TargetFolders = [@"Bilder\Urlaub"],
            AllowedExtensions = [".jpg", ".jpeg"],
            MaxFileSizeMb = 100,
        },
    };
}
