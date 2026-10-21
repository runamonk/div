using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WindowWrangler.Core;

namespace WindowWrangler;

public partial class App : Application
{
    WranglerSettings _settings = new();
    WindowWatcher? _watcher;
    LayoutsWindow? _layoutsWindow;
    SettingsWindow? _settingsWindow;
    Hotkey? _editorHotkey;
    readonly List<Hotkey> _quickKeys = [];
    ArrowKeyHook? _arrowKeys;

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
            };

            LoadSettings();
            _watcher = new WindowWatcher(() => _settings);
            _watcher.Start();
            Log.Write("Started.");

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
        _settings = WranglerSettings.Load(AppPaths.SettingsFile, out var problem);
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
            _settingsWindow = new SettingsWindow(_settings, SaveSettings);
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
        try
        {
            Startup.IsEnabled = !Startup.IsEnabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"Couldn't change start with Windows: {ex.Message}");
        }
        item.IsChecked = Startup.IsEnabled;
    }

    void Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
