using Avalonia.Controls;
using Avalonia.Interactivity;
using WindowWrangler.Core;

namespace WindowWrangler;

// Options that don't belong in the layouts window. Changes only apply on Save.
public partial class SettingsWindow : Window
{
    readonly WranglerSettings _settings;
    readonly Action _save;
    readonly Action _restartAsAdmin;

    // For the XAML previewer.
    public SettingsWindow() : this(new WranglerSettings(), () => { }, () => { }) { }

    public SettingsWindow(WranglerSettings settings, Action save, Action restartAsAdmin)
    {
        InitializeComponent();
        _settings = settings;
        _save = save;
        _restartAsAdmin = restartAsAdmin;

        StartWithWindowsBox.IsChecked = Startup.IsEnabled;
        RunAsAdminBox.IsChecked = settings.RunAsAdmin;
        SnapWithShiftBox.IsChecked = settings.SnapWithShift;
        RestoreSizeBox.IsChecked = settings.RestoreSizeOnUnsnap;
        EdgeReachBox.Value = settings.EdgeReach;
        WinArrowsBox.IsChecked = settings.OverrideWinArrows;
        MaximizeToZoneBox.IsChecked = settings.MaximizeToZone;
        RestoreLastKnownBox.IsChecked = settings.RestoreLastKnown;
        ThresholdSlider.Value = Math.Round(settings.MatchThreshold * 100);
        ThresholdText.Text = $"{ThresholdSlider.Value:0}%";
        ThresholdSlider.ValueChanged += (_, e) => ThresholdText.Text = $"{e.NewValue:0}%";
        ToleranceBox.Value = settings.Tolerance;
        ExcludedBox.Text = string.Join(Environment.NewLine, settings.ExcludedApps);
    }

    async void Save_Click(object? sender, RoutedEventArgs e)
    {
        bool wasAdminMode = _settings.RunAsAdmin;
        _settings.RunAsAdmin = RunAsAdminBox.IsChecked == true;
        _settings.SnapWithShift = SnapWithShiftBox.IsChecked == true;
        _settings.RestoreSizeOnUnsnap = RestoreSizeBox.IsChecked == true;
        _settings.EdgeReach = (int)(EdgeReachBox.Value ?? 0);
        _settings.OverrideWinArrows = WinArrowsBox.IsChecked == true;
        _settings.MaximizeToZone = MaximizeToZoneBox.IsChecked == true;
        _settings.RestoreLastKnown = RestoreLastKnownBox.IsChecked == true;
        _settings.MatchThreshold = ThresholdSlider.Value / 100;
        _settings.Tolerance = (int)(ToleranceBox.Value ?? 0);
        _settings.ExcludedApps = (ExcludedBox.Text ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _save();

        // The admin kind of start-with-Windows can only be set up from an admin copy; until then it's
        // the normal kind, and the admin copy swaps it over when it starts.
        bool startWithWindows = StartWithWindowsBox.IsChecked == true;
        if (startWithWindows != Startup.IsEnabled || _settings.RunAsAdmin != wasAdminMode)
        {
            try
            {
                Startup.Set(startWithWindows, _settings.RunAsAdmin && Elevation.IsAdmin);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
            {
                Log.Write($"Couldn't change start with Windows: {ex.Message}");
            }
        }

        if (_settings.RunAsAdmin && !Elevation.IsAdmin &&
            await ConfirmDialog.Show(this, "Restart as administrator now?", "Windows will ask for permission. Until it restarts, apps running as admin stay unmanaged.", "Restart"))
            _restartAsAdmin();
        Close();
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
