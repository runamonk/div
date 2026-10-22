// DivHook.dll
//
// Div loads this into the apps whose windows it manages (never into everything,
// and never into games). It makes maximizing fill the window's zone instead of the screen:
//
//   - The maximize command (the button, a title bar double-click, the window menu) moves the
//     window into its zone as a normal window, so it keeps Windows 11's rounded corners and
//     border; a maximized window loses them. The next maximize command puts it back.
//   - Anything that maximizes without that command (some keyboard paths) gets a real maximize,
//     sized to the zone: we answer Windows' "how big is maximized?" (WM_GETMINMAXINFO) with the
//     zone, and since some apps (anything on Chromium) ignore that answer, we also put the zone
//     in place at the last moment the window is placed (WM_WINDOWPOSCHANGING).
//
// With Shift held we stay out of it, and the window maximizes to the whole screen.
//
// The zone tag is a window property Div sets from outside: four 16-bit screen
// coordinates (left, top, right, bottom) packed into one value. No property, no change. It may
// also leave the window's own size (from before it was snapped into a zone), width and height.
//
// How we get to answer: Windows loads this DLL into the app through a message hook, but hooks
// may only look at messages, not change them. So the first time a tagged window gets a message,
// we subclass it (put our window procedure in front of the app's), and from then on we see the
// question from the inside.
//
// Keep this small and boring. It runs inside other people's apps; a crash here is their crash.

// Windows 10 1607 or later, for the per-DPI frame metrics. (The compiler sets an older default.)
#undef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <dwmapi.h>

#define ZONE_PROPERTY L"Div.MaximizeZone"
#define OWN_SIZE_PROPERTY L"Div.OwnSize"
// Where the window was before the maximize command moved it into its zone, to go back to.
#define BEFORE_PROPERTY L"Div.BeforeZone"
// Versioned: windows subclassed by an older copy keep it until their app restarts, and a newer
// copy has to be able to go in front of it (and so get the last word) rather than think it's done.
// Bump this whenever answer_with_zone changes.
#define OLD_PROC_PROPERTY L"Div.OldWndProc.1"
// Set on windows that use the older ANSI text mode, so we pass their messages on the same way.
#define ANSI_PROPERTY L"Div.Ansi.1"
// Set while a window is maximized to the whole screen on purpose (Shift was held), so later
// placements while it stays maximized are left alone too.
#define FULL_SCREEN_PROPERTY L"Div.FullScreen"

static int unpack(ULONG_PTR packed, int shift)
{
    return (short)((packed >> shift) & 0xFFFF);
}

static ULONG_PTR pack(RECT r)
{
    return (ULONG_PTR)(USHORT)r.left | (ULONG_PTR)(USHORT)r.top << 16 | (ULONG_PTR)(USHORT)r.right << 32 | (ULONG_PTR)(USHORT)r.bottom << 48;
}

static RECT unpack_rect(ULONG_PTR packed)
{
    RECT r = { unpack(packed, 0), unpack(packed, 16), unpack(packed, 32), unpack(packed, 48) };
    return r;
}

static BOOL shift_held(void)
{
    return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
}

// The window's zone (visible edges, screen coordinates) and the work area of its monitor.
// FALSE when there's no zone, or it isn't on the window's monitor.
static BOOL window_zone(HWND hwnd, RECT *zone, RECT *work)
{
    ULONG_PTR packed = (ULONG_PTR)GetPropW(hwnd, ZONE_PROPERTY);
    if (!packed) return FALSE;
    *zone = unpack_rect(packed);
    if (zone->right <= zone->left || zone->bottom <= zone->top) return FALSE;

    MONITORINFO monitor = { sizeof monitor };
    if (!GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), &monitor)) return FALSE;

    POINT middle = { (zone->left + zone->right) / 2, (zone->top + zone->bottom) / 2 };
    if (!PtInRect(&monitor.rcWork, middle)) return FALSE;
    *work = monitor.rcWork;
    return TRUE;
}

// Where the whole window should be when maximized into its zone, in screen coordinates, and the
// monitor work area it's on. FALSE when there's no zone, or it isn't on the window's monitor.
static BOOL maximized_rect(HWND hwnd, RECT *window, RECT *work)
{
    RECT zone;
    if (!window_zone(hwnd, &zone, work)) return FALSE;

    // A maximized window hangs its frame (the invisible resize borders) off the screen edges, so
    // the zone has to grow by that much. Work it out from the window's style the way Windows does;
    // what the app reports can't be trusted for it, since some apps hand back our last answer.
    // (Left and bottom only: the title bar adds to the top, and that edge isn't used.)
    RECT frame = { 0, 0, 0, 0 };
    if (!AdjustWindowRectExForDpi(&frame, (DWORD)GetWindowLongPtrW(hwnd, GWL_STYLE), FALSE,
                                  (DWORD)GetWindowLongPtrW(hwnd, GWL_EXSTYLE), GetDpiForWindow(hwnd)))
        return FALSE;
    int border_x = -frame.left, border_y = frame.bottom;

    window->left = zone.left - border_x;
    window->top = zone.top - border_y;
    window->right = zone.right + border_x;
    window->bottom = zone.bottom + border_y;
    return TRUE;
}

static BOOL close_to(RECT a, RECT b)
{
    return abs(a.left - b.left) <= 4 && abs(a.top - b.top) <= 4 && abs(a.right - b.right) <= 4 && abs(a.bottom - b.bottom) <= 4;
}

// The maximize command. Instead of maximizing, move the window into its zone as a normal window,
// remembering where it was; if it's already there, put it back. FALSE to let it maximize as usual.
static BOOL maximize_command(HWND hwnd)
{
    if (shift_held() || IsZoomed(hwnd) || IsIconic(hwnd)) return FALSE;
    RECT zone, work;
    if (!window_zone(hwnd, &zone, &work)) return FALSE;

    // What you see (frame) is smaller than the window: it has invisible resize borders round it.
    RECT window, frame;
    if (!GetWindowRect(hwnd, &window)) return FALSE;
    if (FAILED(DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &frame, sizeof frame))) frame = window;
    int left = frame.left - window.left, top = frame.top - window.top;
    int right = window.right - frame.right, bottom = window.bottom - frame.bottom;

    RECT target;
    if (close_to(frame, zone))
    {
        // Already filling its zone: back to where it was, or if we never moved it here, to its own
        // size (or most of the zone, if that's unknown), centred.
        ULONG_PTR before = (ULONG_PTR)GetPropW(hwnd, BEFORE_PROPERTY);
        if (before) target = unpack_rect(before);
        else
        {
            ULONG_PTR own = (ULONG_PTR)GetPropW(hwnd, OWN_SIZE_PROPERTY);
            int width = own ? (int)(own & 0xFFFF) : 0, height = own ? (int)((own >> 16) & 0xFFFF) : 0;
            int zone_width = zone.right - zone.left, zone_height = zone.bottom - zone.top;
            // Unknown, or about the zone's size anyway: something that visibly changes.
            if (width <= 0 || height <= 0 || (width * 20 >= zone_width * 19 && height * 20 >= zone_height * 19))
            {
                width = zone_width * 7 / 10;
                height = zone_height * 7 / 10;
            }
            if (width > zone_width) width = zone_width;
            if (height > zone_height) height = zone_height;
            target.left = zone.left + (zone_width - width) / 2 - left;
            target.top = zone.top + (zone_height - height) / 2 - top;
            target.right = target.left + left + width + right;
            target.bottom = target.top + top + height + bottom;
        }
        RemovePropW(hwnd, BEFORE_PROPERTY);
    }
    else
    {
        SetPropW(hwnd, BEFORE_PROPERTY, (HANDLE)pack(window));
        target.left = zone.left - left;
        target.top = zone.top - top;
        target.right = zone.right + right;
        target.bottom = zone.bottom + bottom;
    }
    SetWindowPos(hwnd, NULL, target.left, target.top, target.right - target.left, target.bottom - target.top,
                 SWP_NOZORDER | SWP_NOACTIVATE);
    return TRUE;
}

// "How big is maximized?" Position is relative to the monitor's work area.
static void answer_size_question(HWND hwnd, MINMAXINFO *info)
{
    RECT window, work;
    if (shift_held() || !maximized_rect(hwnd, &window, &work)) return;
    info->ptMaxPosition.x = window.left - work.left;
    info->ptMaxPosition.y = window.top - work.top;
    info->ptMaxSize.x = window.right - window.left;
    info->ptMaxSize.y = window.bottom - window.top;
}

// The window is about to be placed. If it's maximized and being sized, put it in the zone, whatever
// the app decided. Shift held at the moment it maximizes means whole screen, until it's restored.
static void place_in_zone(HWND hwnd, WINDOWPOS *pos)
{
    if (!IsZoomed(hwnd) || (pos->flags & SWP_NOSIZE) || GetPropW(hwnd, FULL_SCREEN_PROPERTY)) return;
    if (shift_held())
    {
        SetPropW(hwnd, FULL_SCREEN_PROPERTY, (HANDLE)1);
        return;
    }
    RECT window, work;
    if (!maximized_rect(hwnd, &window, &work)) return;
    pos->x = window.left;
    pos->y = window.top;
    pos->cx = window.right - window.left;
    pos->cy = window.bottom - window.top;
    pos->flags &= ~SWP_NOMOVE;
}

// Our window procedure, in front of the app's. Everything goes to the app as normal; we only
// touch the answer to "how big is maximized?" on the way back.
static LRESULT CALLBACK zone_window_proc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    WNDPROC app = (WNDPROC)GetPropW(hwnd, OLD_PROC_PROPERTY);
    BOOL ansi = GetPropW(hwnd, ANSI_PROPERTY) != NULL;
    if (!app) return ansi ? DefWindowProcA(hwnd, message, wParam, lParam) : DefWindowProcW(hwnd, message, wParam, lParam);

    if (message == WM_NCDESTROY)
    {
        // Last message the window gets; tidy our properties away.
        RemovePropW(hwnd, OLD_PROC_PROPERTY);
        RemovePropW(hwnd, ANSI_PROPERTY);
        RemovePropW(hwnd, ZONE_PROPERTY);
        RemovePropW(hwnd, OWN_SIZE_PROPERTY);
        RemovePropW(hwnd, BEFORE_PROPERTY);
        RemovePropW(hwnd, FULL_SCREEN_PROPERTY);
        return ansi ? CallWindowProcA(app, hwnd, message, wParam, lParam) : CallWindowProcW(app, hwnd, message, wParam, lParam);
    }

    // The maximize command never reaches the app if we've dealt with it.
    if (message == WM_SYSCOMMAND && (wParam & 0xFFF0) == SC_MAXIMIZE && maximize_command(hwnd)) return 0;
    // Moved or resized by hand since: "back to where it was" no longer makes sense.
    if (message == WM_EXITSIZEMOVE) RemovePropW(hwnd, BEFORE_PROPERTY);

    // The app has its say first; we get the last word. (Nothing we touch involves text, so ANSI
    // and Unicode windows are handled the same; they just get their messages passed on their way.)
    LRESULT result = ansi ? CallWindowProcA(app, hwnd, message, wParam, lParam) : CallWindowProcW(app, hwnd, message, wParam, lParam);
    if (message == WM_GETMINMAXINFO) answer_size_question(hwnd, (MINMAXINFO *)lParam);
    else if (message == WM_WINDOWPOSCHANGING) place_in_zone(hwnd, (WINDOWPOS *)lParam);
    else if (message == WM_WINDOWPOSCHANGED && !IsZoomed(hwnd)) RemovePropW(hwnd, FULL_SCREEN_PROPERTY);
    return result;
}

// Once a window points at our code, this DLL must never be unloaded from the app, or the next
// message would jump into nothing. Windows would unload it when Div quits, so we
// pin it here: it then stays until the app itself closes.
static BOOL pin_ourselves(void)
{
    static volatile LONG pinned = 0;
    if (pinned) return TRUE;
    HMODULE self;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                            (LPCWSTR)(void *)&zone_window_proc, &self))
        return FALSE;
    InterlockedExchange(&pinned, 1);
    return TRUE;
}

static void subclass(HWND hwnd)
{
    if (GetPropW(hwnd, OLD_PROC_PROPERTY) || !pin_ourselves()) return;
    // Use the same text mode the window already has, so swapping procedures doesn't change it.
    BOOL unicode = IsWindowUnicode(hwnd);
    LONG_PTR app = unicode ? GetWindowLongPtrW(hwnd, GWLP_WNDPROC) : GetWindowLongPtrA(hwnd, GWLP_WNDPROC);
    if (!app || app == (LONG_PTR)zone_window_proc) return;
    // Store everything ours needs before we swap, so it can always find the app's procedure.
    if (!unicode && !SetPropW(hwnd, ANSI_PROPERTY, (HANDLE)1)) return;
    if (!SetPropW(hwnd, OLD_PROC_PROPERTY, (HANDLE)app))
    {
        RemovePropW(hwnd, ANSI_PROPERTY);
        return;
    }
    LONG_PTR swapped = unicode ? SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (LONG_PTR)zone_window_proc)
                               : SetWindowLongPtrA(hwnd, GWLP_WNDPROC, (LONG_PTR)zone_window_proc);
    if (!swapped)
    {
        RemovePropW(hwnd, OLD_PROC_PROPERTY);
        RemovePropW(hwnd, ANSI_PROPERTY);
    }
}

// The way in: Windows calls this inside the app after any message sent to its windows. We only
// use it to notice tagged windows that aren't subclassed yet. Runs on the window's own thread,
// which is the only thread allowed to subclass it.
__declspec(dllexport) LRESULT CALLBACK DivCallWndRetProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION)
    {
        const CWPRETSTRUCT *message = (const CWPRETSTRUCT *)lParam;
        if (GetPropW(message->hwnd, ZONE_PROPERTY)) subclass(message->hwnd);
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    // We never need per-thread notifications; skipping them saves a little work in busy apps.
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
