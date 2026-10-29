using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using PhotoCloud.Core.Configuration;
using PhotoCloud.Core.Logging;
using PhotoCloud.Core.Storage;
using PhotoCloud.Web;

namespace PhotoCloud.Tests;

public class WebHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photocloud-test-" + Guid.NewGuid().ToString("N"));

    public WebHostTests() => Directory.CreateDirectory(Path.Combine(_root, "Bilder"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client, List<LogEntry> Log)> StartAsync(params string[] allowedIps)
    {
        var port = FreePort();
        var config = new PhotoCloudConfig
        {
            Server = new ServerSettings { Port = port, AllowedClientIps = allowedIps.ToList() },
            CloudStorage = new CloudStorageSettings { TargetFolders = ["Bilder"], MaxFileSizeMb = 1 },
        };
        var storage = new CloudStorage(_root, config.CloudStorage.TargetFolders, config.CloudStorage.AllowedExtensions, 1);
        var log = new List<LogEntry>();
        var app = PhotoCloudWebHost.Build(config, storage, e => { lock (log) log.Add(e); });
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") }, log);
    }

    [Fact]
    public async Task Upload_FromAllowedIp_IsStoredAndLogged()
    {
        var (app, client, log) = await StartAsync("127.0.0.1");
        await using (app)
        {
            Assert.Contains("Bilder", await client.GetStringAsync("/api/folders"));
            Assert.Contains("<select", await client.GetStringAsync("/"));

            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent([1, 2, 3]);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "file", "a.jpg");
            var response = await client.PostAsync("/api/upload?folder=Bilder", form);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(File.Exists(Path.Combine(_root, "Bilder", $"{DateTime.Now:yyyyMMdd}_a.jpg")));
            Assert.Contains(log, e => e.Level == LogLevel.Info && e.FileName == $"{DateTime.Now:yyyyMMdd}_a.jpg");
        }
    }

    [Fact]
    public async Task Upload_WithUnknownFolderOrExtension_IsRejected()
    {
        var (app, client, log) = await StartAsync("127.0.0.1");
        await using (app)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "a.exe" } };
            var body = await (await client.PostAsync("/api/upload?folder=Bilder", form)).Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":false", body);

            using var form2 = new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "a.jpg" } };
            body = await (await client.PostAsync("/api/upload?folder=Andere", form2)).Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":false", body);
            Assert.Equal(2, log.Count(e => e.Level == LogLevel.Error));
        }
    }

    [Fact]
    public async Task Request_FromNonWhitelistedIp_IsForbidden()
    {
        var (app, client, log) = await StartAsync("192.168.0.99");
        await using (app)
        {
            var response = await client.GetAsync("/api/folders");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(log, e => e.Level == LogLevel.Error);
        }
    }
}
