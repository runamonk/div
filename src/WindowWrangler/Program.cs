using Avalonia;
using System;
using System.Runtime.Versioning;

// Hooks, hotkeys and the registry: this app only makes sense on Windows.
[assembly: SupportedOSPlatform("windows")]

namespace WindowWrangler;

class Program
{
    // Avalonia and SynchronizationContext-dependent APIs are unavailable until AppMain starts.
    [STAThread]
    public static void Main(string[] args)
    {
        // Two copies would fight over every window.
        using var mutex = new Mutex(true, @"Local\WindowWrangler", out bool firstInstance);
        if (!firstInstance) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
