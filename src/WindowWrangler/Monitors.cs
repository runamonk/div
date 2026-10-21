using System.Runtime.InteropServices;
using WindowWrangler.Core;
using static WindowWrangler.Native;

namespace WindowWrangler;

// Id is the monitor's device path, which survives reboots and docking; the \\.\DISPLAYn name doesn't.
sealed record MonitorInfo(IntPtr Handle, string Id, int Number, Rect Bounds, Rect WorkArea, double Scale, bool IsPrimary);

static class Monitors
{
    // Numbered left to right, then top to bottom.
    public static List<MonitorInfo> All()
    {
        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) => { handles.Add(h); return true; }, IntPtr.Zero);

        return handles.Select(h => Describe(h, 0))
            .OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)
            .Select((m, i) => m with { Number = i + 1 })
            .ToList();
    }

    public static MonitorInfo FromWindow(IntPtr hwnd) => FromHandle(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    public static IntPtr HandleAtCursor()
    {
        GetCursorPos(out var point);
        return MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
    }

    public static MonitorInfo FromPoint(int x, int y) =>
        FromHandle(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    public static MonitorInfo FromHandle(IntPtr handle) => All().FirstOrDefault(m => m.Handle == handle) ?? Describe(handle, 0);

    static MonitorInfo Describe(IntPtr handle, int number)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(handle, ref info);
        double scale = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        return new MonitorInfo(handle, DeviceId(info.szDevice), number,
            ToRect(info.rcMonitor), ToRect(info.rcWork), scale, (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    static string DeviceId(string deviceName)
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(deviceName, i, ref device, EDD_GET_DEVICE_INTERFACE_NAME); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0 && !string.IsNullOrEmpty(device.DeviceID))
                return device.DeviceID;
            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return deviceName;
    }

    static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
