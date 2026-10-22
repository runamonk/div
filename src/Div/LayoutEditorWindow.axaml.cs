using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Div.Core;

namespace Div;

// Covers one monitor's work area with the layout being edited, grid or canvas.
// Edits a copy; Saved hands it back.
public partial class LayoutEditorWindow : Window
{
    const string GridHint = "Click to split  ·  Shift+click splits horizontally  ·  Drag across zones to merge  ·  Drag handles to resize  ·  Esc cancels";
    const string CanvasHint = "Drag a zone to move it  ·  Drag its edges or corners to resize  ·  Delete removes the selected zone  ·  Esc cancels";

    readonly ZoneLayout _layout;
    readonly MonitorInfo _monitor;
    Point? _toolbarGrab;
    Vector _toolbarOffset;

    public event Action<ZoneLayout>? Saved;

    // For the XAML previewer.
    public LayoutEditorWindow() : this(new GridLayout(), Monitors.All()[0]) { }

    internal LayoutEditorWindow(ZoneLayout layout, MonitorInfo monitor)
    {
        InitializeComponent();
        _layout = layout;
        _monitor = monitor;
        NameBox.Text = layout.Name;
        QuickKeyBox.ItemsSource = new[] { "None" }.Concat(Enumerable.Range(0, 10).Select(k => k.ToString())).ToList();
        QuickKeyBox.SelectedIndex = layout.QuickKey is { } key ? key + 1 : 0;

        var area = new PixelSize(monitor.WorkArea.Width, monitor.WorkArea.Height);
        bool isGrid = layout is GridLayout;
        GridEditor.IsVisible = SpacingPanel.IsVisible = isGrid;
        CanvasEditor.IsVisible = CanvasTools.IsVisible = !isGrid;
        Hint.Text = isGrid ? GridHint : CanvasHint;

        if (layout is GridLayout grid)
        {
            GridEditor.Layout = grid;
            GridEditor.AreaSize = area;
            SpacingBox.Value = grid.Spacing;
            SpacingBox.ValueChanged += (_, _) =>
            {
                grid.Spacing = (int)(SpacingBox.Value ?? 0);
                GridEditor.InvalidateVisual();
            };
        }
        else if (layout is CanvasLayout canvas)
        {
            CanvasEditor.Layout = canvas;
            CanvasEditor.AreaSize = area;
        }

        GridEditor.MergeOffered += point =>
        {
            Canvas.SetLeft(MergeButton, point.X + 12);
            Canvas.SetTop(MergeButton, point.Y + 12);
            MergeButton.IsVisible = true;
        };
        GridEditor.MergeCancelled += () => MergeButton.IsVisible = false;

        Toolbar.PointerPressed += Toolbar_PointerPressed;
        Toolbar.PointerMoved += Toolbar_PointerMoved;
        Toolbar.PointerReleased += (_, e) =>
        {
            _toolbarGrab = null;
            e.Pointer.Capture(null);
        };

        FitToMonitor(monitor.Scale);
        // The real scaling is only known once the window is on that monitor.
        Opened += (_, _) =>
        {
            FitToMonitor(RenderScaling);
            Activate();
        };
    }

    void FitToMonitor(double scaling)
    {
        Position = new PixelPoint(_monitor.WorkArea.Left, _monitor.WorkArea.Top);
        Width = _monitor.WorkArea.Width / scaling;
        Height = _monitor.WorkArea.Height / scaling;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }
        // Delete in the name box is for text, not zones.
        if (e.Key == Key.Delete && CanvasEditor.IsVisible && FocusManager?.GetFocusedElement() is not TextBox)
            CanvasEditor.DeleteSelected();
        GridEditor.ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        GridEditor.ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        base.OnKeyUp(e);
    }

    void Toolbar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only the toolbar's own background, not its text box or buttons.
        if (e.Source is not (Border or TextBlock or StackPanel)) return;
        _toolbarGrab = e.GetPosition(this);
        e.Pointer.Capture(Toolbar);
        e.Handled = true;
    }

    void Toolbar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_toolbarGrab is not { } grab) return;
        var point = e.GetPosition(this);
        _toolbarOffset += point - grab;
        _toolbarGrab = point;
        Toolbar.RenderTransform = new TranslateTransform(_toolbarOffset.X, _toolbarOffset.Y);
    }

    void Merge_Click(object? sender, RoutedEventArgs e)
    {
        GridEditor.MergeSelection();
        MergeButton.IsVisible = false;
    }

    void AddZone_Click(object? sender, RoutedEventArgs e) => CanvasEditor.AddZone();

    void DeleteZone_Click(object? sender, RoutedEventArgs e) => CanvasEditor.DeleteSelected();

    void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        _layout.Name = string.IsNullOrEmpty(name) ? "Custom layout" : name;
        _layout.QuickKey = QuickKeyBox.SelectedIndex > 0 ? QuickKeyBox.SelectedIndex - 1 : null;
        Saved?.Invoke(_layout);
        Close();
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
