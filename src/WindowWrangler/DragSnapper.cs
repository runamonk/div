using Avalonia.Threading;
using WindowWrangler.Core;
using static WindowWrangler.Native;

namespace WindowWrangler;

// While a window is being dragged: show the zones (with Shift, by default), light up the one
// under the cursor, and hand back where to drop it. Ctrl stretches across zones from the first one.
sealed class DragSnapper
{
    readonly Func<WranglerSettings> _settings;
    readonly DispatcherTimer _timer;
    ZoneOverlayWindow? _overlay;
    MonitorInfo? _monitor;
    IReadOnlyList<Rect> _zones = [];
    int _anchor = -1;
    Rect? _target;

    public DragSnapper(Func<WranglerSettings> settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Track();
    }

    public void Begin()
    {
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

    void Track()
    {
        bool showing = !_settings().SnapWithShift || WindowOps.IsKeyDown(VK_SHIFT);
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
        // Last zone wins, so on a canvas the one drawn on top is picked.
        int zone = -1;
        for (int i = 0; i < _zones.Count; i++)
            if (x >= _zones[i].Left && x < _zones[i].Right && y >= _zones[i].Top && y < _zones[i].Bottom) zone = i;
        // In a gap between zones, keep whatever was lit.
        if (zone < 0) return;

        bool spanning = WindowOps.IsKeyDown(VK_CONTROL);
        if (!spanning || _anchor < 0) _anchor = zone;
        _target = spanning ? _zones[_anchor].Union(_zones[zone]) : _zones[zone];
        _overlay!.Highlight(_target, _monitor.WorkArea);
    }
}
