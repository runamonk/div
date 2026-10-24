using System.Text.Json;

namespace Div.Core;

public sealed class DivSettings
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Kept as two plain lists rather than one polymorphic one, so the JSON stays simple.
    public List<GridLayout> CustomLayouts { get; set; } = [];
    public List<CanvasLayout> CanvasLayouts { get; set; } = [];
    // Monitor device id -> layout id. Monitors not listed get the default.
    public Dictionary<string, string> MonitorLayouts { get; set; } = [];
    public string DefaultLayoutId { get; set; } = LayoutTemplates.DefaultId;
    // How closely a window must overlap a zone (intersection over union, 0..1) before we snap it.
    public int SnapDistance { get; set; } = 10;
    // Process names we never touch, e.g. "notepad" or "notepad.exe".
    public List<string> ExcludedApps { get; set; } = [];
    // Holding Shift while dragging shows the zones and drops the window into one. Off: drag freely.
    public bool SnapWithShift { get; set; } = true;
    // Put windows that open nowhere near a zone back where that app last was.
    public bool RestoreLastKnown { get; set; } = true;
    // Dragging a window out of its zone puts it back to the size it was before it was snapped.
    public bool RestoreSizeOnUnsnap { get; set; } = true;
    // While dragging, this close (in pixels at 100% scaling) to the edge two zones share lights up both.
    public int EdgeReach { get; set; } = 20;
    // Run with admin rights, so apps running as admin can be managed too.
    public bool RunAsAdmin { get; set; }
    // Win+arrow moves the active window between zones instead of Windows' own snapping.
    public bool OverrideWinArrows { get; set; }
    // Maximizing a window fills its zone instead of the screen; with Shift held it's the whole screen.
    public bool MaximizeToZone { get; set; } = true;
    // Look for a newer <div> on GitHub shortly after starting and once a day.
    public bool CheckForUpdates { get; set; } = true;
    // Square off Windows 11's rounded corners on windows sitting in a zone.
    public bool SquareCorners { get; set; }

    public bool IsExcluded(string processName)
    {
        static string Trim(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        var name = Trim(processName);
        return ExcludedApps.Any(e => string.Equals(Trim(e), name, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<ZoneLayout> UserLayouts => CustomLayouts.Cast<ZoneLayout>().Concat(CanvasLayouts);

    public ZoneLayout? FindLayout(string id) =>
        LayoutTemplates.All.Concat(UserLayouts).FirstOrDefault(l => l.Id == id);

    public string LayoutIdFor(string monitorId) =>
        MonitorLayouts.TryGetValue(monitorId, out var id) && (id == LayoutTemplates.NoneId || FindLayout(id) is not null)
            ? id
            : DefaultLayoutId;

    // Null means the monitor has no layout, so its windows are left alone.
    public ZoneLayout? LayoutFor(string monitorId) => FindLayout(LayoutIdFor(monitorId));

    public ZoneLayout? FindQuickKey(int key) => UserLayouts.FirstOrDefault(l => l.QuickKey == key);

    // Adds a user layout, or replaces the one with the same id.
    public void AddOrUpdate(ZoneLayout layout)
    {
        // A quick key belongs to one layout at a time.
        if (layout.QuickKey is { } key)
            foreach (var other in UserLayouts)
                if (other.Id != layout.Id && other.QuickKey == key) other.QuickKey = null;

        switch (layout)
        {
            case GridLayout grid: AddOrUpdate(CustomLayouts, grid); break;
            case CanvasLayout canvas: AddOrUpdate(CanvasLayouts, canvas); break;
        }
    }

    static void AddOrUpdate<T>(List<T> layouts, T layout) where T : ZoneLayout
    {
        int index = layouts.FindIndex(l => l.Id == layout.Id);
        if (index >= 0) layouts[index] = layout;
        else layouts.Add(layout);
    }

    public void RemoveLayout(string id)
    {
        CustomLayouts.RemoveAll(l => l.Id == id);
        CanvasLayouts.RemoveAll(l => l.Id == id);
        foreach (var monitor in MonitorLayouts.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList())
            MonitorLayouts.Remove(monitor);
        if (DefaultLayoutId == id) DefaultLayoutId = LayoutTemplates.DefaultId;
    }

    // Missing file: write defaults. Broken file: use defaults but leave the file alone so nothing is lost.
    public static DivSettings Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
        {
            var fresh = new DivSettings();
            fresh.Save(path);
            return fresh;
        }

        DivSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<DivSettings>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            problem = $"Couldn't read settings, using defaults: {ex.Message}";
            return new DivSettings();
        }

        settings ??= new DivSettings();
        settings.ExcludedApps ??= [];
        settings.MonitorLayouts ??= [];
        settings.CustomLayouts ??= [];
        settings.CanvasLayouts ??= [];

        var broken = new List<string>();
        DropInvalid(settings.CustomLayouts, broken);
        DropInvalid(settings.CanvasLayouts, broken);
        foreach (var layout in settings.UserLayouts.Where(l => l.QuickKey is < 0 or > 9))
            layout.QuickKey = null;
        if (broken.Count > 0) problem = $"Skipped invalid layouts: {string.Join("; ", broken)}";

        if (settings.DefaultLayoutId != LayoutTemplates.NoneId && settings.FindLayout(settings.DefaultLayoutId ?? "") is null)
            settings.DefaultLayoutId = LayoutTemplates.DefaultId;
        return settings;
    }

    static void DropInvalid<T>(List<T> layouts, List<string> broken) where T : ZoneLayout
    {
        foreach (var layout in layouts.ToList())
        {
            string? error = "missing";
            if (layout is not null && layout.IsValid(out error)) continue;
            broken.Add($"{layout?.Name ?? "(unnamed)"}: {error}");
            layouts.Remove(layout!);
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
