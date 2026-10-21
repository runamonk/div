using Avalonia.Threading;
using WindowWrangler.Core;
using static WindowWrangler.Native;

namespace WindowWrangler;

// Watches windows come, go and get dragged.
//  - New windows: wait for them to stop moving, then nudge them into the zone they're nearly in,
//    or, if they opened nowhere near one, put them back where that app last sat.
//  - Dragged windows: snap to the zone picked on the overlay, or go back to their own size when
//    dragged out of one.
//  - Win+arrow: move the active window to the next zone that way.
//  - Either way, remember where each app's windows end up.
sealed class WindowWatcher : IDisposable
{
    const int TickMs = 100;
    // Apps like Edge show the window and then move it again; wait until it's been still this long.
    const int SettleTicks = 4;
    const int MaxWaitMs = 3000;
    // After placing a new window, look once more in case the app moved it back.
    const int RecheckMs = 600;

    sealed class Pending
    {
        public Rect LastFrame;
        public int StillTicks;
        public int WaitedMs;
    }

    readonly Func<WranglerSettings> _settings;
    readonly WinEventProc _onWinEvent; // Held so the GC doesn't collect the callback under Windows' feet.
    readonly DispatcherTimer _timer;
    readonly Dictionary<IntPtr, Pending> _pending = [];
    // Each window gets corrected once at launch; after that it's the user's business.
    readonly HashSet<IntPtr> _handled = [];
    readonly DragSnapper _snapper;
    readonly AppHistory _history = AppHistory.Load(AppPaths.HistoryFile);
    // The history spot each open window is sitting in, so moving it updates rather than adds.
    readonly Dictionary<IntPtr, (string App, Placement Placement)> _owned = [];
    // Windows we've put in a zone, and the size they were before, for putting it back.
    readonly Dictionary<IntPtr, (Rect Zone, int Width, int Height)> _snapped = [];
    IntPtr _showHook, _moveHook, _dragging;
    Rect _dragStart;
    bool _dragWasSnapped;

    public bool Paused { get; set; }

    public WindowWatcher(Func<WranglerSettings> settings)
    {
        _settings = settings;
        _onWinEvent = OnWinEvent;
        _snapper = new DragSnapper(settings);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += OnTick;
    }

    // Out-of-context hooks call back on this thread's message loop, which is Avalonia's UI thread.
    public void Start()
    {
        _showHook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_SHOW, IntPtr.Zero, _onWinEvent, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _moveHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _onWinEvent, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;

        switch (eventType)
        {
            case EVENT_OBJECT_DESTROY:
                _handled.Remove(hwnd);
                _pending.Remove(hwnd);
                _owned.Remove(hwnd); // The history spot stays; that's the point.
                _snapped.Remove(hwnd);
                return;
            case EVENT_SYSTEM_MOVESIZESTART:
                DragStarted(hwnd);
                return;
            case EVENT_SYSTEM_MOVESIZEEND:
                DragEnded(hwnd);
                return;
        }

        if (Paused || _handled.Contains(hwnd) || _pending.ContainsKey(hwnd)) return;
        if (GetAncestor(hwnd, GA_ROOT) != hwnd) return;

        // Full checks wait until it settles; styles and cloaking can change right after showing.
        _pending[hwnd] = new Pending { LastFrame = WindowOps.GetVisibleFrame(hwnd) };
        _timer.Start();
    }

    void OnTick(object? sender, EventArgs e)
    {
        bool mouseDown = WindowOps.IsMouseDown();
        foreach (var (hwnd, pending) in _pending.ToList())
        {
            if (!IsWindow(hwnd))
            {
                _pending.Remove(hwnd);
                continue;
            }

            pending.WaitedMs += TickMs;
            var frame = WindowOps.GetVisibleFrame(hwnd);
            if (frame == pending.LastFrame && !mouseDown)
                pending.StillTicks++;
            else
            {
                pending.StillTicks = 0;
                pending.LastFrame = frame;
            }

            bool settled = pending.StillTicks >= SettleTicks;
            if (!settled && pending.WaitedMs < MaxWaitMs) continue;

            _pending.Remove(hwnd);
            _handled.Add(hwnd);
            // Never yank a window the user is dragging.
            if (settled && !Paused && Correct(hwnd, atLaunch: true))
                DispatcherTimer.RunOnce(() => { if (IsWindow(hwnd)) Correct(hwnd); }, TimeSpan.FromMilliseconds(RecheckMs));
        }

        if (_pending.Count == 0) _timer.Stop();
    }

    public bool Correct(IntPtr hwnd) => Correct(hwnd, atLaunch: false);

    bool Correct(IntPtr hwnd, bool atLaunch)
    {
        if (!IsManaged(hwnd, out var name)) return false;

        var settings = _settings();
        var frame = WindowOps.GetVisibleFrame(hwnd);
        var monitor = Monitors.FromWindow(hwnd);
        if (frame == monitor.Bounds) return false; // Fullscreen.

        var zones = settings.LayoutFor(monitor.Id)?.GetZones(monitor.WorkArea) ?? [];
        if (ZoneMatcher.FindBest(frame, zones, settings.MatchThreshold) is { } zone)
        {
            Remember(hwnd, monitor, zone);
            if (zone.NearlyEquals(frame, settings.Tolerance)) return false;
            Move(hwnd, name, frame, zone, "nudged");
            return true;
        }

        // Nowhere near a zone. Apps that remember their own spot don't end up here; the lazy ones do.
        return atLaunch && settings.RestoreLastKnown && RestoreLastKnown(hwnd, name, frame, settings);
    }

    bool RestoreLastKnown(IntPtr hwnd, string name, Rect frame, WranglerSettings settings)
    {
        var app = WindowOps.GetAppKey(hwnd);
        var placement = _history.Pick(app, TakenBy(app, hwnd));
        if (placement is null) return false;

        var monitor = Monitors.All().FirstOrDefault(m => m.Id == placement.MonitorId);
        if (monitor is null) return false; // That monitor isn't plugged in.

        // The layout may have changed since, so go to whichever zone fits the old spot best.
        var zones = settings.LayoutFor(monitor.Id)?.GetZones(monitor.WorkArea) ?? [];
        if (ZoneMatcher.FindBest(placement.Frame, zones, settings.MatchThreshold) is not { } zone) return false;

        Move(hwnd, name, frame, zone, "restored");
        Remember(hwnd, monitor, zone);
        return true;
    }

    // Where the app's other open windows are, so a second window doesn't land on the first.
    List<Placement> TakenBy(string app, IntPtr except) =>
        WindowOps.TopLevelWindows()
            .Where(w => w != except && IsWindowVisible(w) && WindowOps.GetAppKey(w) == app)
            .Select(w => new Placement(Monitors.FromWindow(w).Id, WindowOps.GetVisibleFrame(w)))
            .ToList();

    void DragStarted(IntPtr hwnd)
    {
        if (Paused || !IsManaged(hwnd, out _, allowMaximized: true)) return;
        _dragging = hwnd;
        _dragStart = WindowOps.GetVisibleFrame(hwnd);
        // Resized since we snapped it? Then the user has taken over its size.
        _dragWasSnapped = _snapped.TryGetValue(hwnd, out var snapped) && snapped.Zone.NearlyEquals(_dragStart, _settings().Tolerance);
        if (!_dragWasSnapped) _snapped.Remove(hwnd);
        _snapper.Begin(hwnd);
    }

    void DragEnded(IntPtr hwnd)
    {
        if (hwnd != _dragging) return;
        _dragging = IntPtr.Zero;
        var target = _snapper.End();

        if (target is { } zone)
        {
            var frame = WindowOps.GetVisibleFrame(hwnd);
            Move(hwnd, WindowOps.GetProcessName(hwnd), frame, zone, "snapped");
            Remember(hwnd, Monitors.FromPoint((zone.Left + zone.Right) / 2, (zone.Top + zone.Bottom) / 2), zone);
            return;
        }

        // Placed by hand. If it's sitting in a zone remember that, otherwise it's floating now.
        var settings = _settings();
        var dropped = WindowOps.GetVisibleFrame(hwnd);
        if (dropped == _dragStart) return;
        var monitor = Monitors.FromWindow(hwnd);
        var zones = settings.LayoutFor(monitor.Id)?.GetZones(monitor.WorkArea) ?? [];
        if (ZoneMatcher.FindBest(dropped, zones, settings.MatchThreshold) is { } near && near.NearlyEquals(dropped, settings.Tolerance))
        {
            Remember(hwnd, monitor, near);
            return;
        }

        Forget(hwnd);
        if (_dragWasSnapped && settings.RestoreSizeOnUnsnap && _snapped.Remove(hwnd, out var before))
            RestoreSize(hwnd, dropped, before.Width, before.Height);
    }

    // Shrink (or grow) back to the old size around the cursor, so the bit you grabbed stays under it.
    static void RestoreSize(IntPtr hwnd, Rect dropped, int width, int height)
    {
        var (x, _) = WindowOps.CursorPosition();
        double grabbed = dropped.Width > 0 ? (x - dropped.Left) / (double)dropped.Width : 0.5;
        int left = (int)(x - grabbed * width);
        WindowOps.MoveVisibleFrameTo(hwnd, new Rect(left, dropped.Top, left + width, dropped.Top + height));
    }

    // Win+arrow. Zones on every monitor count, so it walks off one screen onto the next.
    // Returns false when there's nothing that way, and Windows gets the key instead.
    public bool MoveInDirection(Direction direction)
    {
        var hwnd = GetForegroundWindow();
        if (Paused || !IsManaged(hwnd, out var name)) return false;

        var settings = _settings();
        var frame = WindowOps.GetVisibleFrame(hwnd);
        var zones = Monitors.All().SelectMany(m => settings.LayoutFor(m.Id)?.GetZones(m.WorkArea) ?? []).ToList();
        if (ZoneMatcher.Neighbour(frame, zones, direction, settings.Tolerance) is not { } zone) return false;

        Move(hwnd, name, frame, zone, "keyboard");
        Remember(hwnd, Monitors.FromPoint((zone.Left + zone.Right) / 2, (zone.Top + zone.Bottom) / 2), zone);
        // Crossing to a monitor with different scaling can leave it a little off; tidy up once it lands.
        DispatcherTimer.RunOnce(() => { if (IsWindow(hwnd)) Correct(hwnd); }, TimeSpan.FromMilliseconds(RecheckMs));
        return true;
    }

    // Layouts were changed or switched; a drag in progress should show the new zones.
    public void LayoutsChanged() => _snapper.Refresh();

    bool IsManaged(IntPtr hwnd, out string name, bool allowMaximized = false)
    {
        name = "";
        if (!WindowOps.IsCandidate(hwnd, allowMaximized)) return false;
        name = WindowOps.GetProcessName(hwnd);
        return !_settings().IsExcluded(name);
    }

    void Move(IntPtr hwnd, string name, Rect from, Rect to, string why)
    {
        // Keep the size from before the first snap; moving between zones shouldn't overwrite it.
        int width = from.Width, height = from.Height;
        if (_snapped.TryGetValue(hwnd, out var earlier)) (width, height) = (earlier.Width, earlier.Height);
        _snapped[hwnd] = (to, width, height);

        WindowOps.MoveVisibleFrameTo(hwnd, to);
        Log.Write($"{name}: {why} {from} -> {to}");
    }

    void Remember(IntPtr hwnd, MonitorInfo monitor, Rect zone)
    {
        var placement = new Placement(monitor.Id, zone);
        bool had = _owned.TryGetValue(hwnd, out var old);
        if (had && old.Placement == placement) return;

        var app = had ? old.App : WindowOps.GetAppKey(hwnd);
        _history.Record(app, placement, had ? old.Placement : null);
        _owned[hwnd] = (app, placement);
        SaveHistory();
    }

    void Forget(IntPtr hwnd)
    {
        if (!_owned.Remove(hwnd, out var old)) return;
        _history.Forget(old.App, old.Placement);
        SaveHistory();
    }

    void SaveHistory()
    {
        try
        {
            _history.Save(AppPaths.HistoryFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Couldn't save history: {ex.Message}");
        }
    }

    public int TidyAll() => WindowOps.TopLevelWindows().Count(Correct);

    public void Dispose()
    {
        _timer.Stop();
        _snapper.End();
        if (_showHook != IntPtr.Zero) UnhookWinEvent(_showHook);
        if (_moveHook != IntPtr.Zero) UnhookWinEvent(_moveHook);
        _showHook = _moveHook = IntPtr.Zero;
    }
}
