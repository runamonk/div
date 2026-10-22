using Avalonia.Threading;
using Div.Core;
using static Div.Native;

namespace Div;

// While a window is being dragged with Shift held: show the zones, light up the one
// under the cursor, and hand back where to drop it. Near the edge two zones share, both light up;
// Ctrl stretches across zones from the first one. Resizing a window never snaps, and with
// drag-snapping turned off in settings, drags are left alone entirely.
sealed class DragSnapper
{
    readonly Func<DivSettings> _settings;
    readonly DispatcherTimer _timer;
    ZoneOverlayWindow? _overlay;
    MonitorInfo? _monitor;
    IReadOnlyList<Rect> _zones = [];
    int _anchor = -1;
    Rect? _target;
    bool _resizing;

    public DragSnapper(Func<DivSettings> settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Track();
    }

    public void Begin(IntPtr hwnd)
    {
        _resizing = GrabbedByEdge(hwnd);
        _monitor = null;
        _anchor = -1;
        _target = null;
        _timer.Start();
        Track();
    }

    // Where the window should go, or null to leave it where the user dropped it.
    public Rect? End()
    {
        _timer.Stop();
        _overlay?.HideZones();
        _monitor = null;
        return _target;
    }

    // Layout changed mid-drag (quick switch): pick the zones up again on the next tick.
    public void Refresh() => _monitor = null;

    // Move and resize both arrive as the same event, so tell them apart by where the cursor
    // grabbed the window. Size can't be used: a maximized window shrinks as soon as it's dragged.
    static bool GrabbedByEdge(IntPtr hwnd)
    {
        var frame = WindowOps.GetVisibleFrame(hwnd);
        var (x, y) = WindowOps.CursorPosition();
        int grip = (int)(8 * Monitors.FromWindow(hwnd).Scale);
        return x < frame.Left + grip || x >= frame.Right - grip || y >= frame.Bottom - grip || y < frame.Top + grip / 2;
    }

    void Track()
    {
        bool showing = _settings().SnapWithShift && WindowOps.IsKeyDown(VK_SHIFT) && !_resizing;
        if (!showing)
        {
            _overlay?.HideZones();
            _monitor = null;
            _target = null;
            _anchor = -1;
            return;
        }

        var handle = Monitors.HandleAtCursor();
        if (_monitor?.Handle != handle)
        {
            _monitor = Monitors.FromHandle(handle);
            _zones = _settings().LayoutFor(_monitor.Id)?.GetZones(_monitor.WorkArea) ?? [];
            _anchor = -1;
            _target = null;
            if (_zones.Count == 0)
            {
                _overlay?.HideZones();
                return;
            }
            _overlay ??= new ZoneOverlayWindow();
            _overlay.ShowZones(_monitor, _zones);
        }
        if (_zones.Count == 0) return;

        var (x, y) = WindowOps.CursorPosition();
        int reach = (int)(_settings().EdgeReach * _monitor.Scale);
        // In a gap between zones, keep whatever was lit.
        if (ZoneMatcher.TargetAt(x, y, _zones, reach) is not { } under) return;

        if (WindowOps.IsKeyDown(VK_CONTROL))
        {
            // Stretch from wherever Ctrl started to here.
            int zone = _zones.ToList().FindLastIndex(z => under.Intersect(z) == z);
            if (_anchor < 0) _anchor = zone;
            _target = _zones[_anchor].Union(under);
        }
        else
        {
            _anchor = -1;
            _target = under;
        }
        _overlay!.Highlight(_target, _monitor.WorkArea);
    }
}
