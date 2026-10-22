using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Div.Core;
using Rect = Avalonia.Rect;
using ZoneRect = Div.Core.Rect;

namespace Div;

// Draws the layout full size and turns mouse input into split / merge / resize.
// The layout works in monitor pixels; this control works in DIPs, hence all the scaling.
sealed class GridEditorCanvas : Control
{
    const double HandleLength = 56, HandleThickness = 10, HandleReach = 10;

    static readonly IBrush ZoneBrush = new SolidColorBrush(Color.FromArgb(0xB8, 0xF3, 0xF3, 0xF3));
    static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0x00, 0x78, 0xD4));
    static readonly IPen ZonePen = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), 1);
    static readonly IBrush NumberBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
    static readonly IPen SplitPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), 2, new DashStyle([4, 3], 0));
    static readonly IBrush HandleBrush = Brushes.White;
    static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), 2);

    readonly HashSet<int> _selection = [];
    int _pressZone = -1;
    bool _selecting;
    Divider? _dragging;
    Point? _hover;
    bool _shiftHeld;

    public GridLayout Layout { get; set; } = new();
    // Size of the monitor work area in pixels.
    public PixelSize AreaSize { get; set; } = new(1, 1);

    public bool ShiftHeld
    {
        get => _shiftHeld;
        set
        {
            if (_shiftHeld == value) return;
            _shiftHeld = value;
            InvalidateVisual();
        }
    }

    public event Action<Point>? MergeOffered;
    public event Action? MergeCancelled;

    double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    List<Rect> Zones()
    {
        double s = Scaling;
        return Layout.GetZones(new ZoneRect(0, 0, AreaSize.Width, AreaSize.Height))
            .Select(z => new Rect(z.Left / s, z.Top / s, z.Width / s, z.Height / s))
            .ToList();
    }

    int ZoneAt(Point p) => Zones().FindIndex(z => z.Contains(p));

    // One handle per stretch of grid line, in its middle.
    List<(Divider Divider, Rect Handle)> Handles()
    {
        double s = Scaling;
        var cols = Layout.ColumnEdges(0, AreaSize.Width);
        var rows = Layout.RowEdges(0, AreaSize.Height);
        return Layout.Dividers().Select(d =>
        {
            if (d.Vertical)
            {
                double x = cols[d.Edge] / s, y = (rows[d.From] + rows[d.To + 1]) / 2.0 / s;
                return (d, new Rect(x - HandleThickness / 2, y - HandleLength / 2, HandleThickness, HandleLength));
            }
            double hy = rows[d.Edge] / s, hx = (cols[d.From] + cols[d.To + 1]) / 2.0 / s;
            return (d, new Rect(hx - HandleLength / 2, hy - HandleThickness / 2, HandleLength, HandleThickness));
        }).ToList();
    }

    Divider? HandleAt(Point p)
    {
        foreach (var (divider, handle) in Handles())
            if (handle.Inflate(HandleReach).Contains(p)) return divider;
        return null;
    }

    // Point to layout units (0..10000) along one axis.
    int ToLayoutX(double x) => (int)Math.Clamp(x * Scaling * GridLayout.Multiplier / AreaSize.Width, 0, GridLayout.Multiplier);
    int ToLayoutY(double y) => (int)Math.Clamp(y * Scaling * GridLayout.Multiplier / AreaSize.Height, 0, GridLayout.Multiplier);

    public void MergeSelection()
    {
        Layout.Merge(_selection);
        _selection.Clear();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        MergeCancelled?.Invoke();
        _selection.Clear();

        if (HandleAt(p) is { } divider)
        {
            _dragging = divider;
            e.Pointer.Capture(this);
        }
        else if ((_pressZone = ZoneAt(p)) >= 0)
        {
            _selecting = true;
            _selection.Add(_pressZone);
            e.Pointer.Capture(this);
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        _hover = p;
        ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (_dragging is { } divider)
            Layout.MoveEdge(divider.Vertical, divider.Edge, divider.Vertical ? ToLayoutX(p.X) : ToLayoutY(p.Y));
        else if (_selecting && ZoneAt(p) is var zone and >= 0)
        {
            _selection.Clear();
            _selection.UnionWith(Layout.MergeCover([_pressZone, zone]));
        }

        var over = _dragging ?? HandleAt(p);
        Cursor = over is { } d ? new Cursor(d.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var p = e.GetPosition(this);
        e.Pointer.Capture(null);

        if (_dragging is not null)
            _dragging = null;
        else if (_selecting)
        {
            _selecting = false;
            if (_selection.Count > 1)
                MergeOffered?.Invoke(p);
            else
            {
                _selection.Clear();
                if (ZoneAt(p) == _pressZone)
                {
                    bool vertical = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    Layout.Split(_pressZone, vertical ? ToLayoutX(p.X) : ToLayoutY(p.Y), vertical);
                }
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Transparent fill so the whole area takes clicks, gaps included.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var zones = Zones();
        for (int i = 0; i < zones.Count; i++)
        {
            bool selected = _selection.Contains(i);
            var box = zones[i].Deflate(2);
            context.DrawRectangle(selected ? SelectedBrush : ZoneBrush, ZonePen, box, 6, 6);

            var number = new FormattedText((i + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 36, selected ? Brushes.White : NumberBrush);
            context.DrawText(number, box.Center - new Point(number.Width / 2, number.Height / 2));
        }

        // Preview where a click would cut.
        if (_hover is { } h && _dragging is null && !_selecting && HandleAt(h) is null && ZoneAt(h) is var zone and >= 0)
        {
            var box = zones[zone];
            if (ShiftHeld) context.DrawLine(SplitPen, new Point(box.Left, h.Y), new Point(box.Right, h.Y));
            else context.DrawLine(SplitPen, new Point(h.X, box.Top), new Point(h.X, box.Bottom));
        }

        foreach (var (_, handle) in Handles())
            context.DrawRectangle(HandleBrush, HandlePen, handle, 4, 4);
    }
}
