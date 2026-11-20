using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PhotoCloud.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private bool _shuttingDown;

    /// <summary>Lädt die XAML-Ressourcen der Anwendung.</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Startet die Anwendung ohne sichtbares Hauptfenster.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow();
            desktop.Startup += (_, _) => _mainWindow.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow(object? sender, EventArgs e) => ShowWindow();

    private void ShowWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private async void ExitApplication(object? sender, EventArgs e) => await ShutdownAsync();

    internal async Task<bool> ShutdownAsync()
    {
        if (_shuttingDown || _mainWindow is null ||
            ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        _shuttingDown = true;
        try
        {
            await _mainWindow.StopAsync();
            desktop.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            _shuttingDown = false;
            _mainWindow.ShowShutdownError(ex.Message);
            ShowWindow();
            return false;
        }
    }
}