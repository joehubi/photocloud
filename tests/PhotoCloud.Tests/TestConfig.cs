using PhotoCloud.Core.Configuration;

namespace PhotoCloud.Tests;

internal static class TestConfig
{
    /// <summary>Finds photocloud.config.json via PHOTOCLOUD_CONFIG or by walking up from the test output folder.</summary>
    public static PhotoCloudConfig? TryLoad()
    {
        var explicitPath = Environment.GetEnvironmentVariable("PHOTOCLOUD_CONFIG");
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return File.Exists(explicitPath) ? PhotoCloudConfig.Load(explicitPath) : null;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, PhotoCloudConfig.FileName);
            if (File.Exists(candidate))
            {
                return PhotoCloudConfig.Load(candidate);
            }
        }

        return null;
    }
}
