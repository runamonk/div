namespace Div.Core;

// Anything that can turn a work area into zones: grids and canvases.
public abstract class ZoneLayout
{
    // Layouts are stored on a 0..10000 scale so they fit any monitor.
    public const int Multiplier = 10000;
    // Smallest row, column or zone the editors will make.
    public const int MinPercent = 250;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom layout";
    // 0-9 for Win+Ctrl+Alt+number, or null for none.
    public int? QuickKey { get; set; }

    // Zone rectangles for a monitor's work area, in zone-number order.
    public abstract IReadOnlyList<Rect> GetZones(Rect workArea);
    public abstract bool IsValid(out string? error);
    public abstract ZoneLayout Clone();
}
