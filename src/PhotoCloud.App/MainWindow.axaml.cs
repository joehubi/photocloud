using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.AspNetCore.Builder;
using PhotoCloud.Core.Configuration;
using PhotoCloud.Core.Logging;
using PhotoCloud.Core.Storage;
using PhotoCloud.Web;

namespace PhotoCloud.App;

/// <summary>Eine Zeile der Log-Liste mit Icon, Farbe und fertigem Text.</summary>
public sealed record LogRow(string Icon, IBrush Brush, string Text, LogLevel Level);

public partial class MainWindow : Window
{
    private const int InitialLogEntries = 500;

    private readonly List<LogEntry> _entries = new();
    private PhotoCloudConfig? _config;
    private UploadLogWriter? _logWriter;
    private WebApplication? _server;
    private DispatcherTimer? _nasTimer;

    /// <summary>Verbindet Filter, Start- und Schließen-Ereignisse; die eigentliche Initialisierung folgt in StartAsync.</summary>
    public MainWindow()
    {
        InitializeComponent();
        LevelFilter.SelectionChanged += (_, _) => RefreshList();
        Opened += async (_, _) => await StartAsync();
        Closing += async (_, _) =>
        {
            _nasTimer?.Stop();
            if (_server is not null)
            {
                _logWriter?.Write(new LogEntry(DateTime.Now, LogLevel.Info, "", "", 0, "Anwendung wird beendet."));
                await _server.StopAsync();
            }
        };
    }

    /// <summary>Einmaliger Start: Config und Log laden, Cloud prüfen, Web-Server starten, Cloud-Timer anlegen.</summary>
    private async Task StartAsync()
    {
        var configPath = PhotoCloudConfig.Locate();
        if (configPath is null)
        {
            StatusText.Text = $"✖ {PhotoCloudConfig.FileName} nicht gefunden (neben der Anwendung bzw. in einem übergeordneten Ordner, oder per PHOTOCLOUD_CONFIG).";
            return;
        }

        try
        {
            _config = PhotoCloudConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✖ Konfiguration konnte nicht gelesen werden: {ex.Message}";
            return;
        }

        SettingsBox.Text = FormatSettings(_config, configPath);
        _logWriter = new UploadLogWriter(Path.Combine(Path.GetDirectoryName(configPath)!, "logs"));
        _entries.AddRange(UploadLogReader.ReadLast(_logWriter.LogDirectory, InitialLogEntries));
        RefreshList();

        var nasOnline = await Task.Run(CheckNas);
        var storage = new CloudStorage(_config.CloudSettings.SharePath, _config.CloudStorage.TargetFolders,
            _config.CloudStorage.AllowedExtensions, _config.CloudStorage.MaxFileSizeMb);

        try
        {
            _server = PhotoCloudWebHost.Build(_config, storage, OnLog);
            await _server.StartAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✖ Server konnte nicht gestartet werden (Port {_config.Server.Port} belegt?): {ex.Message}";
            OnLog(new LogEntry(DateTime.Now, LogLevel.Error, "", "", 0, $"Server konnte nicht gestartet werden: {ex.Message}"));
            return;
        }

        OnLog(new LogEntry(DateTime.Now, LogLevel.Info, "", "", 0,
            $"Anwendung gestartet (Port {_config.Server.Port}, Cloud {(nasOnline ? "verbunden" : "nicht erreichbar")})."));
        ShowStatus(nasOnline);
        _nasTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _nasTimer.Tick += async (_, _) => ShowStatus(await Task.Run(CheckNas));
        _nasTimer.Start();
    }

    /// <summary>Verbindet sich mit der Cloud und prüft, ob die Freigabe erreichbar ist; Fehler zählen als offline.</summary>
    private bool CheckNas()
    {
        try
        {
            SmbConnection.Connect(_config!.CloudSettings);
            return Directory.Exists(_config.CloudSettings.SharePath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Zeigt Server-URL, Cloud-Status und die Smartphone-Adresse(n) in der Statuszeile.</summary>
    private void ShowStatus(bool nasOnline)
    {
        var port = _config!.Server.Port;
        var nas = nasOnline ? "✔ Cloud verbunden" : "✖ Cloud nicht erreichbar";
        var phoneUrls = LocalIPv4Addresses().Select(ip => $"http://{ip}:{port}").ToList();
        var phone = phoneUrls.Count > 0 ? string.Join("  oder  ", phoneUrls) : "keine Netzwerkverbindung gefunden";
        StatusText.Text = $"Server läuft: http://127.0.0.1:{port}   |   {nas} ({_config.CloudSettings.SharePath}){Environment.NewLine}Adresse für das Smartphone: {phone}";
    }

    /// <summary>IPv4-Adressen aller aktiven Adapter mit Standardgateway (ohne Loopback und Tunnel).</summary>
    private static IEnumerable<string> LocalIPv4Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(a => a.Address.ToString());

    /// <summary>Log-Callback des Web-Servers: schreibt in die Logdatei und aktualisiert die Liste auf dem UI-Thread.</summary>
    private void OnLog(LogEntry entry)
    {
        _logWriter?.Write(entry);
        Dispatcher.UIThread.Post(() =>
        {
            _entries.Add(entry);
            RefreshList();
        });
    }

    /// <summary>Baut die Log-Liste nach aktuellem Filter neu auf (neueste Einträge oben).</summary>
    private void RefreshList()
    {
        var filter = LevelFilter.SelectedIndex;
        LogList.ItemsSource = _entries
            .Where(e => filter == 0 || (filter == 1 ? e.Level == LogLevel.Error : e.Level == LogLevel.Info))
            .Reverse()
            .Select(ToRow)
            .ToList();
    }

    /// <summary>Wandelt einen Logeintrag in eine Anzeigezeile mit Icon und Farbe je Level.</summary>
    private static LogRow ToRow(LogEntry e)
    {
        var isError = e.Level == LogLevel.Error;
        var details = string.Join("  ", new[] { e.Folder, e.FileName }.Where(s => s.Length > 0));
        var size = e.SizeBytes > 0 ? $"  ({e.SizeBytes / 1024.0 / 1024.0:0.0} MB)" : "";
        var text = $"{e.Timestamp:yyyy-MM-dd HH:mm:ss}  {details}{size}  {e.Message}";
        return new LogRow(isError ? "✖" : "ℹ", isError ? Brushes.Firebrick : Brushes.SteelBlue, text, e.Level);
    }

    /// <summary>Formatiert die Config schreibgeschützt für die Anzeige; das Passwort bleibt maskiert.</summary>
    private static string FormatSettings(PhotoCloudConfig c, string path)
    {
        var cloud = c.CloudSettings;
        var lines = new List<string>
        {
            $"Konfigurationsdatei: {path}",
            "",
            "[Cloud-Settings]",
            $"  Host:              {cloud.Host}",
            $"  Port:              {cloud.Port}",
            $"  Share:             {cloud.Share}",
            $"  User:              {cloud.User}",
            "  Password:          ********",
            "",
            "[Server]",
            $"  Port:              {c.Server.Port}",
            $"  AllowedClientIps:  {FormatList(c.Server.AllowedClientIps)}",
            "",
            "[Cloud-Storage]",
            $"  TargetFolders:     {FormatList(c.CloudStorage.TargetFolders)}",
            $"  MaxFileSizeMb:     {c.CloudStorage.MaxFileSizeMb}",
            $"  AllowedExtensions: {FormatList(c.CloudStorage.AllowedExtensions)}",
            "",
            "[Unittest]",
            $"  TargetFolder:      {c.Unittest.TargetFolder}",
        };
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Stellt jeden Listeneintrag in eine eigene Zeile, eingerückt unter dem Attributnamen.</summary>
    private static string FormatList(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0
            ? "-"
            : string.Concat(list.Select(i => Environment.NewLine + "    - " + i));
    }
}
