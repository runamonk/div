using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Div.Core;

namespace Div;

public partial class App : Application
{
    DivSettings _settings = new();
    WindowWatcher? _watcher;
    LayoutsWindow? _layoutsWindow;
    SettingsWindow? _settingsWindow;
    Hotkey? _editorHotkey;
    readonly List<Hotkey> _quickKeys = [];
    ArrowKeyHook? _arrowKeys;
    Updates? _updates;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray only, no main window.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                _watcher?.Dispose();
                _editorHotkey?.Dispose();
                _arrowKeys?.Dispose();
                foreach (var hotkey in _quickKeys) hotkey.Dispose();
                _updates?.Dispose();
                _updates?.StartInstaller();
            };

            LoadSettings();
            _watcher = new WindowWatcher(() => _settings);
            _watcher.Start();
            Log.Write($"Started {Updates.AppVersion}{(Elevation.IsAdmin ? " (as admin)" : "")}.");
            SetTrayStatus(null);
            _updates = new Updates(() => _settings, SetTrayStatus, () => desktop.Shutdown());

            Rename.MoveStartup(_settings.RunAsAdmin && Elevation.IsAdmin);
            // Only an admin copy can set up (or take down) the admin kind of start-with-Windows.
            if (Elevation.IsAdmin) TryStartup(() => Startup.Match(_settings.RunAsAdmin));

            // Win+Shift+` like FancyZones.
            _editorHotkey = new Hotkey(Native.MOD_WIN | Native.MOD_SHIFT, Native.VK_OEM_3, ShowLayouts);
            if (!_editorHotkey.IsRegistered) Log.Write("Win+Shift+` is taken by another app (FancyZones?), so it won't open the editor.");

            if (StartupItem() is { } startup) startup.IsChecked = Startup.IsEnabled;
            ApplySettings();
        }

        base.OnFrameworkInitializationCompleted();
    }

    void LoadSettings()
    {
        _settings = DivSettings.Load(AppPaths.SettingsFile, out var problem);
        if (problem is not null) Log.Write(problem);
    }

    void SaveSettings()
    {
        try
        {
            _settings.Save(AppPaths.SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Couldn't save settings: {ex.Message}");
        }
        ApplySettings();
    }

    // Things that hang off settings rather than reading them as they go.
    void ApplySettings()
    {
        _watcher?.LayoutsChanged();
        RegisterQuickKeys();

        if (_settings.OverrideWinArrows && _arrowKeys is null && _watcher is not null)
        {
            _watcher.WarmUp();
            _arrowKeys = new ArrowKeyHook(_watcher.MoveInDirection);
            if (!_arrowKeys.IsInstalled) Log.Write("Couldn't hook Win+arrow keys.");
        }
        else if (!_settings.OverrideWinArrows && _arrowKeys is not null)
        {
            _arrowKeys.Dispose();
            _arrowKeys = null;
        }
    }

    // Win+Ctrl+Alt+number for each layout that has a quick key.
    void RegisterQuickKeys()
    {
        foreach (var hotkey in _quickKeys) hotkey.Dispose();
        _quickKeys.Clear();
        foreach (var layout in _settings.UserLayouts.Where(l => l.QuickKey is not null))
        {
            int key = layout.QuickKey!.Value;
            var hotkey = new Hotkey(Native.MOD_WIN | Native.MOD_CONTROL | Native.MOD_ALT, (uint)('0' + key), () => QuickSwitch(key));
            if (!hotkey.IsRegistered) Log.Write($"Win+Ctrl+Alt+{key} is taken by another app.");
            _quickKeys.Add(hotkey);
        }
    }

    // Applies the layout to the monitor with the active window (or the one a window is being dragged on).
    void QuickSwitch(int key)
    {
        if (_settings.FindQuickKey(key) is not { } layout) return;
        var foreground = Native.GetForegroundWindow();
        var monitor = foreground != IntPtr.Zero ? Monitors.FromWindow(foreground) : Monitors.FromHandle(Monitors.HandleAtCursor());

        _settings.MonitorLayouts[monitor.Id] = layout.Id;
        SaveSettings();
        _layoutsWindow?.Refresh();
        ZoneOverlayWindow.Flash(monitor, layout.GetZones(monitor.WorkArea));
        Log.Write($"Switched monitor {monitor.Number} to {layout.Name}.");
    }

    void ShowLayouts()
    {
        if (_layoutsWindow is null)
        {
            _layoutsWindow = new LayoutsWindow(() => _settings, SaveSettings);
            _layoutsWindow.Closed += (_, _) => _layoutsWindow = null;
        }
        _layoutsWindow.Show();
        _layoutsWindow.Activate();
    }

    void Settings_Click(object? sender, EventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, SaveSettings, RestartAsAdmin);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                // The dialog can flip this too; keep the tray tick in step.
                if (StartupItem() is { } startup) startup.IsChecked = Startup.IsEnabled;
            };
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    void Tray_Clicked(object? sender, EventArgs e) => ShowLayouts();

    // Version, admin, and what the updater's up to, if anything.
    void SetTrayStatus(string? status)
    {
        if (TrayIcon.GetIcons(this)?.FirstOrDefault() is not { } tray) return;
        tray.ToolTipText = $"<div> {Updates.AppVersion}{(Elevation.IsAdmin ? " (admin)" : "")}{(status is null ? "" : " - " + status)}";
    }

    async void CheckForUpdates_Click(object? sender, EventArgs e)
    {
        if (_updates is not null) await _updates.CheckNow();
    }

    void EditLayouts_Click(object? sender, EventArgs e) => ShowLayouts();

    void TidyAll_Click(object? sender, EventArgs e)
    {
        int moved = _watcher?.TidyAll() ?? 0;
        Log.Write($"Tidy all moved {moved} window(s).");
    }

    void Pause_Click(object? sender, EventArgs e)
    {
        if (_watcher is null || sender is not NativeMenuItem item) return;
        _watcher.Paused = !_watcher.Paused;
        item.IsChecked = _watcher.Paused;
    }

    void ReloadSettings_Click(object? sender, EventArgs e)
    {
        LoadSettings();
        ApplySettings();
        _layoutsWindow?.Refresh();
        Log.Write("Settings reloaded.");
    }

    void OpenFolder_Click(object? sender, EventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Folder);
        Process.Start(new ProcessStartInfo(AppPaths.Folder) { UseShellExecute = true });
    }

    NativeMenuItem? StartupItem() =>
        TrayIcon.GetIcons(this)?.FirstOrDefault()?.Menu?.Items.OfType<NativeMenuItem>()
            .FirstOrDefault(i => i.Header == "Start with Windows");

    void Startup_Click(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem item) return;
        TryStartup(() => Startup.Set(!Startup.IsEnabled, _settings.RunAsAdmin && Elevation.IsAdmin));
        item.IsChecked = Startup.IsEnabled;
    }

    static void TryStartup(Action change)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            Log.Write($"Couldn't change start with Windows: {ex.Message}");
        }
    }

    // Hand over to an admin copy (Windows asks first) and close this one. Stays put if the answer's no.
    void RestartAsAdmin()
    {
        if (!Elevation.RelaunchAsAdmin())
        {
            Log.Write("Didn't get admin rights; carrying on without.");
            return;
        }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    void Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
