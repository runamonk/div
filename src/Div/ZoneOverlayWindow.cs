using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using static Div.Native;
using Rect = Avalonia.Rect;
using ZoneRect = Div.Core.Rect;

namespace Div;

// See-through window over one monitor showing its zones. Used while dragging a window
// and to flash a layout when it's applied. Never takes focus.
sealed class ZoneOverlayWindow : Window
{
    readonly ZoneOverlay _view = new();
    IntPtr _monitor;

    public ZoneOverlayWindow()
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Content = _view;
        Opened += (_, _) =>
        {
            // Keep it out of Alt+Tab and never let it steal focus.
            if (TryGetPlatformHandle()?.Handle is { } hwnd)
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        };
    }

    public void ShowZones(MonitorInfo monitor, IReadOnlyList<ZoneRect> zones)
    {
        var work = monitor.WorkArea;
        _view.Scale = monitor.Scale;
        _view.Zones = zones.Select(z => new ZoneRect(z.Left - work.Left, z.Top - work.Top, z.Right - work.Left, z.Bottom - work.Top)).ToList();
        _view.Target = null;

        if (_monitor != monitor.Handle)
        {
            _monitor = monitor.Handle;
            Position = new PixelPoint(work.Left, work.Top);
            Width = work.Width / monitor.Scale;
            Height = work.Height / monitor.Scale;
        }
        _view.InvalidateVisual();
        if (!IsVisible) Show();
    }

    // Lights up every zone inside target (screen pixels). Null lights nothing.
    public void Highlight(ZoneRect? target, ZoneRect workArea)
    {
        var relative = target is { } t ? new ZoneRect(t.Left - workArea.Left, t.Top - workArea.Top, t.Right - workArea.Left, t.Bottom - workArea.Top) : (ZoneRect?)null;
        if (relative == _view.Target) return;
        _view.Target = relative;
        _view.InvalidateVisual();
    }

    public void HideZones()
    {
        if (IsVisible) Hide();
    }

    // Brief look at a layout on its monitor, then gone.
    public static void Flash(MonitorInfo monitor, IReadOnlyList<ZoneRect> zones)
    {
        var overlay = new ZoneOverlayWindow();
        overlay.ShowZones(monitor, zones);
        DispatcherTimer.RunOnce(overlay.Close, TimeSpan.FromMilliseconds(700));
    }
}

sealed class ZoneOverlay : Control
{
    static readonly IBrush ZoneBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xF3, 0xF3, 0xF3));
    static readonly IBrush HotBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x78, 0xD4));
    static readonly IPen ZonePen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x30, 0x30, 0x30)), 1.5);
    static readonly IPen HotPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), 3);

    public List<ZoneRect> Zones { get; set; } = [];
    public ZoneRect? Target { get; set; }
    public double Scale { get; set; } = 1;

    public override void Render(DrawingContext context)
    {
        for (int i = 0; i < Zones.Count; i++)
        {
            var z = Zones[i];
            bool hot = Target is { } t && t.Intersect(z) == z;
            var box = new Rect(z.Left / Scale, z.Top / Scale, z.Width / Scale, z.Height / Scale).Deflate(3);
            context.DrawRectangle(hot ? HotBrush : ZoneBrush, hot ? HotPen : ZonePen, box, 8, 8);

            var number = new FormattedText((i + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 40, hot ? Brushes.White : Brushes.Black);
            context.DrawText(number, box.Center - new Point(number.Width / 2, number.Height / 2));
        }
    }
}
