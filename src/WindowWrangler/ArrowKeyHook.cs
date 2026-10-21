using System.Runtime.InteropServices;
using WindowWrangler.Core;
using static WindowWrangler.Native;

namespace WindowWrangler;

// Catches Win+arrow before Windows' own snapping does. RegisterHotKey can't take keys Windows
// already owns, so this needs a low-level keyboard hook. The handler says whether it moved
// something; if not, the key goes through and Windows does its usual thing.
sealed class ArrowKeyHook : IDisposable
{
    readonly Func<Direction, bool> _handler;
    readonly LowLevelKeyboardProc _proc; // Held so the GC doesn't collect the callback.
    IntPtr _hook;

    public ArrowKeyHook(Func<Direction, bool> handler)
    {
        _handler = handler;
        _proc = OnKey;
        // Runs on this thread's message loop. Windows drops hooks that take too long, so keep it quick.
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (ToDirection(key.vkCode) is { } direction && (key.flags & LLKHF_INJECTED) == 0 && OnlyWinHeld() && _handler(direction))
            {
                SwallowWin();
                return 1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static Direction? ToDirection(uint vk) => (int)vk switch
    {
        VK_LEFT => Direction.Left,
        VK_RIGHT => Direction.Right,
        VK_UP => Direction.Up,
        VK_DOWN => Direction.Down,
        _ => null,
    };

    // Win+Shift+arrow (move to monitor) and friends stay with Windows.
    static bool OnlyWinHeld() =>
        (WindowOps.IsKeyDown(VK_LWIN) || WindowOps.IsKeyDown(VK_RWIN)) &&
        !WindowOps.IsKeyDown(VK_SHIFT) && !WindowOps.IsKeyDown(VK_CONTROL) && !WindowOps.IsKeyDown(VK_MENU);

    // Windows opens Start when Win goes down and up with nothing in between. We ate the arrow,
    // so slip in a meaningless key press to make it look like a combination.
    static void SwallowWin()
    {
        var inputs = new INPUT[2];
        inputs[0].type = inputs[1].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = inputs[1].u.ki.wVk = VK_NONE;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
