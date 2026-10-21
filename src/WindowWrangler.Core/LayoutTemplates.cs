namespace WindowWrangler.Core;

// Built-in layouts. These are shared instances; clone before editing.
public static class LayoutTemplates
{
    // "No layout" isn't a ZoneLayout; a monitor set to it is simply left alone.
    public const string NoneId = "template-none";
    public const string DefaultId = "template-columns";

    public static IReadOnlyList<ZoneLayout> All { get; } =
    [
        Named(new CanvasLayout { Zones = [new(1500, 1500, 6500, 7500), new(2000, 2000, 7000, 8000), new(2500, 2500, 7500, 8500)] }, "template-focus", "Focus"),
        Named(GridLayout.CreateColumns(3), DefaultId, "Columns"),
        Named(GridLayout.CreateRows(3), "template-rows", "Rows"),
        Named(GridLayout.CreateGrid(2, 2), "template-grid", "Grid"),
        Named(new GridLayout { ColumnPercents = [2500, 5000, 2500], CellMap = [[0, 1, 2]] }, "template-priority", "Priority Grid"),
    ];

    static ZoneLayout Named(ZoneLayout layout, string id, string name)
    {
        layout.Id = id;
        layout.Name = name;
        return layout;
    }
}
