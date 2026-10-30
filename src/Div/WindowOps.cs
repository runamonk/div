using System.Diagnostics;
using System.Runtime.InteropServices;
using Div.Core;
using static Div.Native;

namespace Div;

static class WindowOps
{
    // Normal resizable app windows only: no dialogs, popups, tool windows or hidden UWP frames.
    // Maximized windows are skipped unless asked: dragging one un-maximizes it, so that's fine to snap.
    // Cloaked ones too if asked: an app can open a window cloaked while it finishes drawing it.
    public static bool IsCandidate(IntPtr hwnd, bool allowMaximized = false, bool allowCloaked = false)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || (IsZoomed(hwnd) && !allowMaximized)) return false;
        if (GetAncestor(hwnd, GA_ROOT) != hwnd || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;

        long style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        long exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((style & WS_CHILD) != 0 || (style & WS_CAPTION) != WS_CAPTION || (style & WS_THICKFRAME) == 0) return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;

        return allowCloaked || !IsCloaked(hwnd);
    }

    static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    // What you actually see. GetWindowRect includes the invisible resize borders on Win10/11.
    public static Rect GetVisibleFrame(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) == 0)
            return ToRect(frame);
        GetWindowRect(hwnd, out var rect);
        return ToRect(rect);
    }

    // Puts the visible frame on target by growing the window by its invisible borders.
    public static void MoveVisibleFrameTo(IntPtr hwnd, Rect target)
    {
        GetWindowRect(hwnd, out var window);
        var frame = GetVisibleFrame(hwnd);
        int left = frame.Left - window.Left, top = frame.Top - window.Top;
        int right = window.Right - frame.Right, bottom = window.Bottom - frame.Bottom;

        // Async so a hung app can't hang us.
        SetWindowPos(hwnd, IntPtr.Zero, target.Left - left, target.Top - top,
            target.Width + left + right, target.Height + top + bottom,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    // Windows 11 rounds window corners, which leaves little gaps between zoned windows.
    // Does nothing on Windows 10.
    public static void SetSquareCorners(IntPtr hwnd, bool square)
    {
        int preference = square ? DWMWCP_DONOTROUND : DWMWCP_DEFAULT;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    // Store apps (Settings, Xbox, Calculator...) all live in frames owned by ApplicationFrameHost,
    // so name them after the app inside instead. While suspended there's no app inside, and it's
    // ApplicationFrameHost after all.
    // Windows animates a window going in and out of maximized; this switches that off for it.
    public static void SetAnimations(IntPtr hwnd, bool on)
    {
        int disabled = on ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int));
    }

    public static string GetProcessName(IntPtr hwnd)
    {
        var className = new System.Text.StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() == "ApplicationFrameWindow" &&
            FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null) is var app && app != IntPtr.Zero)
            hwnd = app;

        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    // What history remembers an app by. Class is included so an app's main window and its
    // odd extra windows don't share spots.
    public static string GetAppKey(IntPtr hwnd)
    {
        var className = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        return $"{GetProcessName(hwnd)}|{className}";
    }

    // Windows won't let a normal process move an admin app's windows, so don't try; leave them
    // to Windows' own maximize and snapping. Running Div as admin lifts this.
    public static bool CanMove(IntPtr hwnd)
    {
        if (Environment.IsPrivilegedProcess) return true;
        GetWindowThreadProcessId(hwnd, out uint pid);
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            // Not allowed to look at its token is as good as a yes.
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return false;
            try
            {
                return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated == 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static bool IsMouseDown() => IsKeyDown(VK_LBUTTON);

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static List<IntPtr> TopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) => { windows.Add(hwnd); return true; }, IntPtr.Zero);
        return windows;
    }

    static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
