using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public sealed record LogRow(string Icon, IBrush Brush, string Text, LogLevel Level);

public partial class MainWindow : Window
{
    private const int InitialLogEntries = 500;

    private readonly List<LogEntry> _entries = new();
    private PhotoCloudConfig? _config;
    private UploadLogWriter? _logWriter;
    private WebApplication? _server;
    private DispatcherTimer? _nasTimer;

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
                await _server.StopAsync();
            }
        };
    }

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
            return;
        }

        ShowStatus(nasOnline);
        _nasTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _nasTimer.Tick += async (_, _) => ShowStatus(await Task.Run(CheckNas));
        _nasTimer.Start();
    }

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

    private void ShowStatus(bool nasOnline)
    {
        var port = _config!.Server.Port;
        var nas = nasOnline ? "✔ Cloud verbunden" : "✖ Cloud nicht erreichbar";
        StatusText.Text = $"Server läuft: http://127.0.0.1:{port}   |   {nas} ({_config.CloudSettings.SharePath})";
    }

    private void OnLog(LogEntry entry)
    {
        _logWriter?.Write(entry);
        Dispatcher.UIThread.Post(() =>
        {
            _entries.Add(entry);
            RefreshList();
        });
    }

    private void RefreshList()
    {
        var filter = LevelFilter.SelectedIndex;
        LogList.ItemsSource = _entries
            .Where(e => filter == 0 || (filter == 1 ? e.Level == LogLevel.Error : e.Level == LogLevel.Info))
            .Reverse()
            .Select(ToRow)
            .ToList();
    }

    private static LogRow ToRow(LogEntry e)
    {
        var isError = e.Level == LogLevel.Error;
        var details = string.Join("  ", new[] { e.Folder, e.FileName }.Where(s => s.Length > 0));
        var size = e.SizeBytes > 0 ? $"  ({e.SizeBytes / 1024.0 / 1024.0:0.0} MB)" : "";
        var text = $"{e.Timestamp:yyyy-MM-dd HH:mm:ss}  {details}{size}  {e.Message}";
        return new LogRow(isError ? "✖" : "ℹ", isError ? Brushes.Firebrick : Brushes.SteelBlue, text, e.Level);
    }

    private static string FormatSettings(PhotoCloudConfig c, string path) => string.Join(Environment.NewLine,
        $"Konfiguration:   {path}",
        $"Cloud:           {c.CloudSettings.SharePath}  (Benutzer: {c.CloudSettings.User}, Passwort: ********)",
        $"Server-Port:     {c.Server.Port}",
        $"Erlaubte IPs:    {string.Join(", ", c.Server.AllowedClientIps)}",
        $"Zielordner:      {string.Join(", ", c.CloudStorage.TargetFolders)}",
        $"Max. Dateigröße: {c.CloudStorage.MaxFileSizeMb} MB",
        $"Dateitypen:      {string.Join(", ", c.CloudStorage.AllowedExtensions)}");
}
