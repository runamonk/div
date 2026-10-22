using Avalonia;
using System;
using System.Runtime.Versioning;
using WindowWrangler.Core;

// Hooks, hotkeys and the registry: this app only makes sense on Windows.
[assembly: SupportedOSPlatform("windows")]

namespace WindowWrangler;

class Program
{
    // Avalonia and SynchronizationContext-dependent APIs are unavailable until AppMain starts.
    [STAThread]
    public static void Main(string[] args)
    {
        // Two copies would fight over every window. A copy we started ourselves (a restart) waits
        // for the old one to finish closing.
        bool restarted = args.Contains(Elevation.RestartedArg);
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, @"Local\WindowWrangler");
        }
        catch (UnauthorizedAccessException)
        {
            return; // An admin copy is running, and a normal one isn't even allowed to look.
        }
        using var disposeMutex = mutex;
        if (!TryOwn(mutex, restarted ? 5000 : 0)) return;

        // Set to run as admin and not yet: hand over to an admin copy (Windows asks first).
        // If the answer's no, carry on without.
        if (!Elevation.IsAdmin && WranglerSettings.Load(AppPaths.SettingsFile, out _).RunAsAdmin)
        {
            mutex.ReleaseMutex();
            if (Elevation.RelaunchAsAdmin()) return;
            if (!TryOwn(mutex, 0)) return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    static bool TryOwn(Mutex mutex, int waitMs)
    {
        try
        {
            return mutex.WaitOne(waitMs);
        }
        catch (AbandonedMutexException)
        {
            return true; // The last copy crashed; it's ours now.
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
