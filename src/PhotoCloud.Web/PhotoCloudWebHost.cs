using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using LogLevel = PhotoCloud.Core.Logging.LogLevel;
using PhotoCloud.Core.Configuration;
using PhotoCloud.Core.Logging;
using PhotoCloud.Core.Storage;

namespace PhotoCloud.Web;

/// <summary>Builds the embeddable upload server (IP whitelist, folder list, streamed upload, phone form).</summary>
public static class PhotoCloudWebHost
{
    private const long MultipartOverheadBytes = 1024 * 1024;

    /// <summary>Baut den Server (Kestrel auf IPv4, IP-Whitelist, Endpunkte); gestartet wird er vom Aufrufer.</summary>
    public static WebApplication Build(PhotoCloudConfig config, CloudStorage storage, Action<LogEntry> log)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, config.Server.Port);
            options.Limits.MaxRequestBodySize = config.CloudStorage.MaxFileSizeMb * 1024L * 1024L + MultipartOverheadBytes;
        });

        var app = builder.Build();
        var allowed = config.Server.AllowedClientIps
            .Select(ip => IPAddress.TryParse(ip, out var parsed) ? parsed : null)
            .OfType<IPAddress>()
            .ToHashSet();
        var indexHtml = LoadIndexHtml(config);
        var favicon = LoadResource("favicon.png");

        app.Use(async (context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is { IsIPv4MappedToIPv6: true })
            {
                remote = remote.MapToIPv4();
            }

            if (remote is null || !allowed.Contains(remote))
            {
                log(new LogEntry(DateTime.Now, LogLevel.Error, "", "", 0, $"Zugriff abgelehnt: IP {remote} nicht erlaubt."));
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });

        app.MapGet("/", () => Results.Content(indexHtml, "text/html; charset=utf-8"));
        app.MapGet("/favicon.png", () => Results.Bytes(favicon, "image/png"));
        app.MapGet("/favicon.ico", () => Results.Bytes(favicon, "image/png"));
        app.MapGet("/api/folders", () => Results.Json(storage.Folders));
        app.MapPost("/api/upload", (Delegate)((HttpContext context) => HandleUploadAsync(context, storage, log)));
        return app;
    }

    /// <summary>Liest die multipart-Anfrage als Stream und speichert jede Datei; antwortet mit einem Ergebnis pro Datei.</summary>
    private static async Task<IResult> HandleUploadAsync(HttpContext context, CloudStorage storage, Action<LogEntry> log)
    {
        var request = context.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || mediaType.Boundary.Length == 0)
        {
            return Results.BadRequest(new { error = "multipart/form-data erwartet." });
        }

        var folder = request.Query["folder"].ToString();
        var reader = new MultipartReader(HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value!, request.Body);
        var results = new List<object>();
        var cancellation = context.RequestAborted;

        try
        {
            while (await reader.ReadNextSectionAsync(cancellation) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || disposition.FileName.Value is not { Length: > 0 } fileName)
                {
                    continue;
                }

                results.Add(await SaveOneAsync(storage, log, folder, fileName, section.Body, cancellation));
            }
        }
        catch (OperationCanceledException)
        {
            log(new LogEntry(DateTime.Now, LogLevel.Error, folder, "", 0, "Upload abgebrochen."));
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or BadHttpRequestException)
        {
            log(new LogEntry(DateTime.Now, LogLevel.Error, folder, "", 0, $"Upload fehlgeschlagen: {ex.Message}"));
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Json(new { files = results });
    }

    /// <summary>Speichert eine Datei über CloudStorage, schreibt einen Info- oder Error-Logeintrag und liefert das Ergebnis für die Antwort.</summary>
    private static async Task<object> SaveOneAsync(CloudStorage storage, Action<LogEntry> log, string folder, string fileName, Stream body, CancellationToken cancellation)
    {
        try
        {
            var saved = await storage.SaveAsync(folder, fileName, body, cancellation);
            log(new LogEntry(DateTime.Now, LogLevel.Info, saved.Folder, saved.FileName, saved.SizeBytes, "Hochgeladen."));
            return new { name = fileName, ok = true, savedAs = saved.FileName, size = saved.SizeBytes };
        }
        catch (UploadRejectedException ex)
        {
            log(new LogEntry(DateTime.Now, LogLevel.Error, folder, fileName, 0, ex.Message));
            return new { name = fileName, ok = false, error = ex.Message };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            const string message = "Cloud nicht erreichbar oder Schreiben fehlgeschlagen.";
            log(new LogEntry(DateTime.Now, LogLevel.Error, folder, fileName, 0, $"{message} {ex.Message}"));
            return new { name = fileName, ok = false, error = message };
        }
    }

    /// <summary>Liest eine eingebettete Ressource dieser Assembly als Bytes.</summary>
    private static byte[] LoadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} fehlt als eingebettete Ressource.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Lädt die Webmaske und setzt Dateityp-Filter und Größenlimit aus der Config ein.</summary>
    private static string LoadIndexHtml(PhotoCloudConfig config)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("index.html")
            ?? throw new InvalidOperationException("index.html fehlt als eingebettete Ressource.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var accept = string.Join(",", config.CloudStorage.AllowedExtensions);
        return reader.ReadToEnd()
            .Replace("{{ACCEPT}}", accept)
            .Replace("{{MAXMB}}", config.CloudStorage.MaxFileSizeMb.ToString());
    }
}
