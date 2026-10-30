using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
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
    private PhotoCloudConfig? _savedConfig;
    private string? _configPath;
    private UploadLogWriter? _logWriter;
    private WebApplication? _server;
    private DispatcherTimer? _nasTimer;
    private bool _loadingSettings;
    private bool _settingsDirty;
    private bool _restartRequired;

    /// <summary>Verbindet Filter, Start- und Schließen-Ereignisse; die eigentliche Initialisierung folgt in StartAsync.</summary>
    public MainWindow()
    {
        InitializeComponent();
        LevelFilter.SelectionChanged += (_, _) => RefreshList();
        SaveSettingsButton.Click += (_, _) => TrySaveSettings();
        RestartSettingsButton.Click += (_, _) => RestartApplication();
        foreach (var input in new[]
        {
            CloudHostBox,
            CloudShareBox,
            CloudUserBox,
            CloudPasswordBox,
            ServerPortBox,
            AllowedClientIpsBox,
            TargetFoldersBox,
            MaxFileSizeBox,
            AllowedExtensionsBox,
        })
        {
            input.TextChanged += (_, _) => MarkSettingsChanged();
        }

        ClearCloudPasswordCheckBox.IsCheckedChanged += (_, _) =>
            SetPasswordClearing(ClearCloudPasswordCheckBox.IsChecked == true);
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
            _configPath = Path.GetFullPath(configPath);
            _config = PhotoCloudConfig.Load(_configPath);
            _savedConfig = _config;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✖ Konfiguration konnte nicht gelesen werden: {ex.Message}";
            return;
        }

        PopulateSettings(_config, _configPath);
        _logWriter = new UploadLogWriter(Path.Combine(Path.GetDirectoryName(_configPath)!, "logs"));
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

    private void PopulateSettings(PhotoCloudConfig config, string path)
    {
        _loadingSettings = true;
        SettingsConfigPath.Text = $"Konfigurationsdatei: {path}";
        CloudHostBox.Text = config.CloudSettings.Host;
        CloudShareBox.Text = config.CloudSettings.Share;
        CloudUserBox.Text = config.CloudSettings.User;
        CloudPasswordBox.Text = "";
        ClearCloudPasswordCheckBox.IsChecked = false;
        CloudPasswordBox.IsEnabled = true;
        CloudPortNotice.Text =
            $"Cloud-Settings.Port in der Datei: {config.CloudSettings.Port}. Dieser Wert wird derzeit nicht angewendet; SMB verwendet den Standardport 445.";
        ServerPortBox.Text = config.Server.Port.ToString(CultureInfo.InvariantCulture);
        AllowedClientIpsBox.Text = string.Join(Environment.NewLine, config.Server.AllowedClientIps);
        TargetFoldersBox.Text = string.Join(Environment.NewLine, config.CloudStorage.TargetFolders);
        MaxFileSizeBox.Text = config.CloudStorage.MaxFileSizeMb.ToString(CultureInfo.InvariantCulture);
        AllowedExtensionsBox.Text = string.Join(Environment.NewLine, config.CloudStorage.AllowedExtensions);
        SettingsPanel.IsEnabled = true;
        _settingsDirty = false;
        _restartRequired = false;
        RestartSettingsButton.IsVisible = false;
        SettingsMessage.Text = "Eine Zeile pro IP-Adresse, Zielordner oder Dateiendung.";
        SettingsMessage.Foreground = Brushes.DimGray;
        _loadingSettings = false;
    }

    private void MarkSettingsChanged()
    {
        if (_loadingSettings)
        {
            return;
        }

        _settingsDirty = true;
        if (_settingsDirty && _restartRequired)
        {
            SetSettingsMessage(
                "Es gibt ungespeicherte Änderungen. Bereits gespeicherte Einstellungen erfordern weiterhin einen Neustart.",
                Brushes.DarkOrange);
        }
        else if (_settingsDirty)
        {
            SetSettingsMessage("Es gibt ungespeicherte Änderungen.", Brushes.DarkOrange);
        }
    }

    private void SetPasswordClearing(bool clear)
    {
        CloudPasswordBox.IsEnabled = !clear;
        if (clear)
        {
            CloudPasswordBox.Text = "";
        }

        MarkSettingsChanged();
    }

    private bool TrySaveSettings()
    {
        if (_config is null || _configPath is null)
        {
            SetSettingsMessage("Es ist keine geladene Konfiguration zum Speichern vorhanden.", Brushes.Firebrick);
            return false;
        }

        var inputErrors = new List<string>();
        var serverPortValid = TryParseInteger(ServerPortBox.Text, "Server-Port", inputErrors, out var serverPort);
        var maxFileSizeValid = TryParseInteger(MaxFileSizeBox.Text, "Maximale Bildgröße", inputErrors, out var maxFileSizeMb);
        if (!serverPortValid || !maxFileSizeValid)
        {
            SetSettingsMessage(string.Join(Environment.NewLine, inputErrors), Brushes.Firebrick);
            return false;
        }

        var config = new PhotoCloudConfig
        {
            CloudSettings = new CloudSettings
            {
                Host = CloudHostBox.Text?.Trim() ?? "",
                Port = _savedConfig!.CloudSettings.Port,
                Share = CloudShareBox.Text?.Trim() ?? "",
                User = CloudUserBox.Text?.Trim() ?? "",
                Password = ClearCloudPasswordCheckBox.IsChecked == true
                    ? ""
                    : string.IsNullOrEmpty(CloudPasswordBox.Text)
                        ? _savedConfig!.CloudSettings.Password
                        : CloudPasswordBox.Text,
            },
            Server = new ServerSettings
            {
                Port = serverPort,
                AllowedClientIps = ReadLines(AllowedClientIpsBox.Text),
            },
            CloudStorage = new CloudStorageSettings
            {
                TargetFolders = ReadLines(TargetFoldersBox.Text),
                MaxFileSizeMb = maxFileSizeMb,
                AllowedExtensions = ReadLines(AllowedExtensionsBox.Text),
            },
        };

        var validationErrors = config.Validate();
        if (validationErrors.Count > 0)
        {
            SetSettingsMessage(
                "Bitte korrigiere folgende Einstellungen:" + Environment.NewLine + string.Join(Environment.NewLine, validationErrors),
                Brushes.Firebrick);
            return false;
        }

        try
        {
            PhotoCloudConfig.Save(_configPath, config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            SetSettingsMessage($"Konfiguration konnte nicht gespeichert werden: {ex.Message}", Brushes.Firebrick);
            return false;
        }

        _savedConfig = config;
        _settingsDirty = false;
        _restartRequired = !RuntimeSettingsEqual(_config, config);
        RestartSettingsButton.IsVisible = _restartRequired;
        SetSettingsMessage(_restartRequired
                ? "Gespeichert. Die laufende App verwendet noch die alten Werte; starte PhotoCloud neu, um die Änderungen anzuwenden."
                : "Gespeichert. Die Einstellungen entsprechen bereits der laufenden App; ein Neustart ist nicht erforderlich.",
            Brushes.DarkGreen);
        return true;
    }

    private static bool RuntimeSettingsEqual(PhotoCloudConfig current, PhotoCloudConfig saved) =>
        string.Equals(current.CloudSettings.Host, saved.CloudSettings.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(current.CloudSettings.Share, saved.CloudSettings.Share, StringComparison.OrdinalIgnoreCase)
        && string.Equals(current.CloudSettings.User, saved.CloudSettings.User, StringComparison.Ordinal)
        && string.Equals(current.CloudSettings.Password, saved.CloudSettings.Password, StringComparison.Ordinal)
        && current.Server.Port == saved.Server.Port
        && current.Server.AllowedClientIps.SequenceEqual(saved.Server.AllowedClientIps, StringComparer.OrdinalIgnoreCase)
        && current.CloudStorage.TargetFolders.SequenceEqual(saved.CloudStorage.TargetFolders, StringComparer.OrdinalIgnoreCase)
        && current.CloudStorage.MaxFileSizeMb == saved.CloudStorage.MaxFileSizeMb
        && current.CloudStorage.AllowedExtensions.SequenceEqual(saved.CloudStorage.AllowedExtensions, StringComparer.OrdinalIgnoreCase);

    private static bool TryParseInteger(string? text, string label, ICollection<string> errors, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        errors.Add($"{label} muss eine ganze Zahl sein.");
        return false;
    }

    private static List<string> ReadLines(string? text) =>
        (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void RestartApplication()
    {
        if (!TrySaveSettings() || !_restartRequired)
        {
            return;
        }

        try
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Der Pfad der laufenden Anwendung konnte nicht ermittelt werden.");
            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
            }

            foreach (var argument in Program.StartupArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add(Program.RestartWaitArgument);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            using var restartedProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Der neue PhotoCloud-Prozess konnte nicht gestartet werden.");
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or ArgumentException)
        {
            SetSettingsMessage($"Neustart konnte nicht gestartet werden: {ex.Message}", Brushes.Firebrick);
        }
    }

    private void SetSettingsMessage(string message, IBrush foreground)
    {
        SettingsMessage.Text = message;
        SettingsMessage.Foreground = foreground;
    }
}
