using System.Security.Cryptography;
using System.ComponentModel;
using PhotoCloud.Core.Storage;

namespace PhotoCloud.Tests;

public class CloudUploadTests
{
    private const string IntegrationTargetFolder = @"03_Bilder\###photocloud";

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task DefaultPhoto_IsCopiedAndRemainsInCloudUnittestFolder()
    {
        var config = TestConfig.TryLoad();
        Skip.If(config is null, "photocloud.config.json nicht gefunden.");

        try
        {
            SmbConnection.Connect(config.CloudSettings);
        }
        catch (IOException ex) when (ex.InnerException is Win32Exception
        {
            NativeErrorCode: 53 or 67 or 1222 or 1231 or 1232
        })
        {
            Skip.If(true, $"NAS nicht erreichbar: {ex.Message}");
        }

        var folderPath = Path.Combine(config!.CloudSettings.SharePath, IntegrationTargetFolder);
        Skip.IfNot(Directory.Exists(folderPath), $"Testordner '{folderPath}' existiert nicht oder ist nicht erreichbar.");

        var storage = new CloudStorage(
            config.CloudSettings.SharePath,
            new[] { IntegrationTargetFolder },
            config.CloudStorage.AllowedExtensions,
            config.CloudStorage.MaxFileSizeMb);

        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "unittest.jpg");
        SaveResult result;
        await using (var input = File.OpenRead(source))
        {
            result = await storage.SaveAsync(IntegrationTargetFolder, "unittest.jpg", input);
        }

        var targetFile = Path.Combine(folderPath, result.FileName);
        Assert.True(File.Exists(targetFile), $"Datei fehlt auf dem Share: {targetFile}");
        Assert.Equal(new FileInfo(source).Length, result.SizeBytes);
        Assert.Equal(Hash(source), Hash(targetFile));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
