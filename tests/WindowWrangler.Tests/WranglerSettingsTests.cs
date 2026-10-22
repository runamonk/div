using WindowWrangler.Core;

namespace WindowWrangler.Tests;

public sealed class WranglerSettingsTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "WindowWranglerTests", Guid.NewGuid().ToString("N"));
    string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void MissingFile_WritesDefaults()
    {
        var settings = WranglerSettings.Load(SettingsPath, out var problem);

        Assert.Null(problem);
        Assert.True(File.Exists(SettingsPath));
        Assert.Equal(LayoutTemplates.DefaultId, settings.LayoutIdFor("any monitor"));
    }

    [Fact]
    public void SavedSettings_RoundTrip()
    {
        var custom = GridLayout.CreateColumns(2);
        var saved = new WranglerSettings { SnapDistance = 25, ExcludedApps = ["notepad"], CustomLayouts = [custom] };
        saved.MonitorLayouts[@"\\?\DISPLAY#GSM5B09#1"] = custom.Id;
        saved.Save(SettingsPath);

        var loaded = WranglerSettings.Load(SettingsPath, out var problem);

        Assert.Null(problem);
        Assert.Equal(25, loaded.SnapDistance);
        Assert.Equal(["notepad"], loaded.ExcludedApps);
        Assert.Equal(2, loaded.LayoutFor(@"\\?\DISPLAY#GSM5B09#1")?.GetZones(new Rect(0, 0, 100, 100)).Count);
    }

    [Fact]
    public void CanvasLayouts_RoundTripAndCanBeApplied()
    {
        var canvas = new CanvasLayout { Name = "Overlap", Zones = [new(0, 0, 6000, 6000), new(4000, 4000, 10000, 10000)] };
        var saved = new WranglerSettings { CanvasLayouts = [canvas] };
        saved.MonitorLayouts["m"] = canvas.Id;
        saved.Save(SettingsPath);

        var loaded = WranglerSettings.Load(SettingsPath, out var problem);

        Assert.Null(problem);
        var layout = Assert.IsType<CanvasLayout>(loaded.LayoutFor("m"));
        Assert.Equal(canvas.Zones, layout.Zones);
    }

    [Fact]
    public void AddOrUpdate_PutsEachTypeInItsListAndReplacesById()
    {
        var settings = new WranglerSettings();
        var grid = GridLayout.CreateColumns(2);
        var canvas = new CanvasLayout();

        settings.AddOrUpdate(grid);
        settings.AddOrUpdate(canvas);
        var edited = grid.Clone();
        edited.Name = "Edited";
        settings.AddOrUpdate(edited);

        Assert.Equal(["Edited"], settings.CustomLayouts.Select(l => l.Name));
        Assert.Single(settings.CanvasLayouts);
        Assert.Equal(2, settings.UserLayouts.Count());
    }

    [Fact]
    public void QuickKey_MovesToTheLayoutThatLastClaimedIt()
    {
        var settings = new WranglerSettings();
        var first = GridLayout.CreateColumns(2);
        first.QuickKey = 3;
        settings.AddOrUpdate(first);
        var second = new CanvasLayout { QuickKey = 3 };

        settings.AddOrUpdate(second);

        Assert.Null(first.QuickKey);
        Assert.Same(second, settings.FindQuickKey(3));
    }

    [Fact]
    public void QuickKey_SurvivesEditingACopy()
    {
        var layout = GridLayout.CreateColumns(2);
        layout.QuickKey = 5;

        Assert.Equal(5, layout.Clone().QuickKey);
        Assert.Equal(5, new CanvasLayout { QuickKey = 5 }.Clone().QuickKey);
    }

    [Fact]
    public void OutOfRangeQuickKey_IsDroppedOnLoad()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{ "customLayouts": [ { "id": "a", "name": "A", "quickKey": 42 } ] }""");

        var settings = WranglerSettings.Load(SettingsPath, out _);

        Assert.Null(settings.CustomLayouts[0].QuickKey);
    }

    [Fact]
    public void BrokenFile_UsesDefaultsAndKeepsTheFile()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = WranglerSettings.Load(SettingsPath, out var problem);

        Assert.NotNull(problem);
        Assert.Empty(settings.CustomLayouts);
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void InvalidCustomLayout_IsSkipped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """
            { "customLayouts": [
                { "id": "bad", "name": "Bad", "columnPercents": [1, 2], "cellMap": [[0, 1]] },
                { "id": "good", "name": "Good" } ] }
            """);

        var settings = WranglerSettings.Load(SettingsPath, out var problem);

        Assert.Contains("Bad", problem);
        Assert.Equal(["good"], settings.CustomLayouts.Select(l => l.Id));
    }

    [Fact]
    public void NoLayout_MeansNoZones()
    {
        var settings = new WranglerSettings();
        settings.MonitorLayouts["m"] = LayoutTemplates.NoneId;

        Assert.Equal(LayoutTemplates.NoneId, settings.LayoutIdFor("m"));
        Assert.Null(settings.LayoutFor("m"));
    }

    [Fact]
    public void RemovingALayout_SendsItsMonitorsBackToTheDefault()
    {
        var custom = GridLayout.CreateColumns(2);
        var settings = new WranglerSettings { CustomLayouts = [custom] };
        settings.MonitorLayouts["m"] = custom.Id;

        settings.RemoveLayout(custom.Id);

        Assert.Empty(settings.CustomLayouts);
        Assert.Equal(LayoutTemplates.DefaultId, settings.LayoutIdFor("m"));
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("notepad.exe")]
    public void Exclusions_IgnoreCaseAndExtension(string processName)
    {
        var settings = new WranglerSettings { ExcludedApps = ["notepad.EXE"] };

        Assert.True(settings.IsExcluded(processName));
        Assert.False(settings.IsExcluded("notepad++"));
    }
}
