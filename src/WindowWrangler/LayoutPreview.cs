using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WindowWrangler.Core;
using Rect = Avalonia.Rect;
using ZoneRect = WindowWrangler.Core.Rect;

namespace WindowWrangler;

// Thumbnail of a layout for the cards. A null layout draws the "No layout" blank.
sealed class LayoutPreview : Control
{
    public static readonly StyledProperty<ZoneLayout?> LayoutProperty =
        AvaloniaProperty.Register<LayoutPreview, ZoneLayout?>(nameof(Layout));

    static LayoutPreview() => AffectsRender<LayoutPreview>(LayoutProperty);

    public ZoneLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Layout is null)
        {
            context.DrawRectangle(Brush("AppEmptyBrush", Brushes.WhiteSmoke), null, bounds, 4, 4);
            return;
        }

        var layout = Layout;
        if (layout is GridLayout grid)
        {
            // Real spacing is in screen pixels and would swamp a thumbnail, so use a fixed small gap instead.
            grid = grid.Clone();
            grid.Spacing = 0;
            layout = grid;
        }
        // Work at 10x so integer rounding doesn't show.
        var zones = layout.GetZones(new ZoneRect(0, 0, (int)(bounds.Width * 10), (int)(bounds.Height * 10)));
        var brush = Brush("AppZoneBrush", Brushes.LightGray);
        // Canvas zones overlap, so outline them to tell them apart.
        var pen = layout is CanvasLayout ? new Pen(Brush("AppCardBrush", Brushes.White), 1.5) : null;
        foreach (var z in zones)
            context.DrawRectangle(brush, pen, new Rect(z.Left / 10.0, z.Top / 10.0, z.Width / 10.0, z.Height / 10.0).Deflate(1.5), 3, 3);
    }

    IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
}
