using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Div.Core;
using Rect = Avalonia.Rect;
using ZoneRect = Div.Core.Rect;

namespace Div;

// Free-floating zones: drag one to move it, drag its edges or corners to resize. Zones can overlap;
// the later one is on top. The layout works on the 0..10000 scale; this control works in DIPs.
sealed class CanvasEditor : Control
{
    // How close to an edge (in DIPs) counts as grabbing it.
    const double Grip = 10;

    static readonly IBrush ZoneBrush = new SolidColorBrush(Color.FromArgb(0xB8, 0xF3, 0xF3, 0xF3));
    static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0x00, 0x78, 0xD4));
    static readonly IPen ZonePen = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), 1.5);
    static readonly IPen SelectedPen = new Pen(Brushes.White, 2);
    static readonly IBrush NumberBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));

    int _selected = -1;
    int _dragging = -1;
    ZoneEdges _edges;
    Point _start;
    ZoneRect _startZone;

    public CanvasLayout Layout { get; set; } = new();
    // Size of the monitor work area in pixels.
    public PixelSize AreaSize { get; set; } = new(1, 1);

    double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    double UnitsToDipX => AreaSize.Width / Scaling / ZoneLayout.Multiplier;
    double UnitsToDipY => AreaSize.Height / Scaling / ZoneLayout.Multiplier;

    Rect ToDip(ZoneRect z) => new(z.Left * UnitsToDipX, z.Top * UnitsToDipY, z.Width * UnitsToDipX, z.Height * UnitsToDipY);

    public void AddZone()
    {
        _selected = Layout.AddZone();
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (Layout.RemoveZone(_selected)) _selected = -1;
        InvalidateVisual();
    }

    // Topmost zone under the point, and which of its edges are within grabbing distance.
    (int Index, ZoneEdges Edges) HitTest(Point p)
    {
        for (int i = Layout.Zones.Count - 1; i >= 0; i--)
        {
            var box = ToDip(Layout.Zones[i]);
            if (!box.Inflate(Grip / 2).Contains(p)) continue;

            var edges = ZoneEdges.None;
            if (Math.Abs(p.X - box.Left) < Grip) edges |= ZoneEdges.Left;
            else if (Math.Abs(p.X - box.Right) < Grip) edges |= ZoneEdges.Right;
            if (Math.Abs(p.Y - box.Top) < Grip) edges |= ZoneEdges.Top;
            else if (Math.Abs(p.Y - box.Bottom) < Grip) edges |= ZoneEdges.Bottom;
            return (i, edges);
        }
        return (-1, ZoneEdges.None);
    }

    static Cursor CursorFor(int index, ZoneEdges edges) => new(edges switch
    {
        _ when index < 0 => StandardCursorType.Arrow,
        ZoneEdges.Left | ZoneEdges.Top => StandardCursorType.TopLeftCorner,
        ZoneEdges.Right | ZoneEdges.Bottom => StandardCursorType.BottomRightCorner,
        ZoneEdges.Right | ZoneEdges.Top => StandardCursorType.TopRightCorner,
        ZoneEdges.Left | ZoneEdges.Bottom => StandardCursorType.BottomLeftCorner,
        ZoneEdges.Left or ZoneEdges.Right => StandardCursorType.SizeWestEast,
        ZoneEdges.Top or ZoneEdges.Bottom => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.SizeAll,
    });

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var (index, edges) = HitTest(p);
        _selected = index;
        if (index >= 0)
        {
            _dragging = index;
            _edges = edges;
            _start = p;
            _startZone = Layout.Zones[index];
            e.Pointer.Capture(this);
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_dragging < 0)
        {
            var (index, edges) = HitTest(p);
            Cursor = CursorFor(index, edges);
            return;
        }

        int dx = (int)((p.X - _start.X) / UnitsToDipX), dy = (int)((p.Y - _start.Y) / UnitsToDipY);
        if (_edges == ZoneEdges.None) Layout.Move(_dragging, _startZone, dx, dy);
        else Layout.Resize(_dragging, _startZone, _edges, dx, dy);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = -1;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        // Transparent fill so empty space takes clicks too (to deselect).
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        for (int i = 0; i < Layout.Zones.Count; i++)
        {
            bool selected = i == _selected;
            var box = ToDip(Layout.Zones[i]);
            context.DrawRectangle(selected ? SelectedBrush : ZoneBrush, selected ? SelectedPen : ZonePen, box, 6, 6);

            var number = new FormattedText((i + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 36, selected ? Brushes.White : NumberBrush);
            context.DrawText(number, box.Center - new Point(number.Width / 2, number.Height / 2));
        }
    }
}
