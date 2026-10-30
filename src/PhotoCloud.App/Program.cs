using Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace PhotoCloud.App;

class Program
{
    internal const string RestartWaitArgument = "--photocloud-wait-for-pid";
    internal static IReadOnlyList<string> StartupArguments { get; private set; } = Array.Empty<string>();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <summary>Startet die Desktop-App (klassischer Avalonia-Desktop-Lifetime).</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        StartupArguments = WaitForPreviousProcess(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(StartupArguments.ToArray());
    }

    private static string[] WaitForPreviousProcess(string[] args)
    {
        var waitIndex = Array.IndexOf(args, RestartWaitArgument);
        if (waitIndex < 0)
        {
            return args;
        }

        if (waitIndex + 1 >= args.Length
            || !int.TryParse(args[waitIndex + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            throw new ArgumentException($"Nach {RestartWaitArgument} muss eine Prozess-ID folgen.", nameof(args));
        }

        var remainingArguments = args.Where((_, index) => index != waitIndex && index != waitIndex + 1).ToArray();
        try
        {
            using var previousProcess = Process.GetProcessById(processId);
            previousProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            return remainingArguments;
        }

        return remainingArguments;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    /// <summary>Konfiguriert Avalonia (Plattform, Schrift, Logging); wird auch vom Designer genutzt.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
