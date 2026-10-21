using Avalonia.Controls;
using Avalonia.Interactivity;
using WindowWrangler.Core;

namespace WindowWrangler;

// Options that don't belong in the layouts window. Changes only apply on Save.
public partial class SettingsWindow : Window
{
    readonly WranglerSettings _settings;
    readonly Action _save;

    // For the XAML previewer.
    public SettingsWindow() : this(new WranglerSettings(), () => { }) { }

    public SettingsWindow(WranglerSettings settings, Action save)
    {
        InitializeComponent();
        _settings = settings;
        _save = save;

        StartWithWindowsBox.IsChecked = Startup.IsEnabled;
        SnapWithShiftBox.IsChecked = settings.SnapWithShift;
        RestoreLastKnownBox.IsChecked = settings.RestoreLastKnown;
        ThresholdSlider.Value = Math.Round(settings.MatchThreshold * 100);
        ThresholdText.Text = $"{ThresholdSlider.Value:0}%";
        ThresholdSlider.ValueChanged += (_, e) => ThresholdText.Text = $"{e.NewValue:0}%";
        ToleranceBox.Value = settings.Tolerance;
        ExcludedBox.Text = string.Join(Environment.NewLine, settings.ExcludedApps);
    }

    void Save_Click(object? sender, RoutedEventArgs e)
    {
        _settings.SnapWithShift = SnapWithShiftBox.IsChecked == true;
        _settings.RestoreLastKnown = RestoreLastKnownBox.IsChecked == true;
        _settings.MatchThreshold = ThresholdSlider.Value / 100;
        _settings.Tolerance = (int)(ToleranceBox.Value ?? 0);
        _settings.ExcludedApps = (ExcludedBox.Text ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _save();

        bool startWithWindows = StartWithWindowsBox.IsChecked == true;
        if (startWithWindows != Startup.IsEnabled)
        {
            try
            {
                Startup.IsEnabled = startWithWindows;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                Log.Write($"Couldn't change start with Windows: {ex.Message}");
            }
        }
        Close();
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
