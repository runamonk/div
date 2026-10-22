using System.Runtime.InteropServices;
using Avalonia.Threading;
using Div.Core;
using static Div.Native;

namespace Div;

// Catches Win+arrow before Windows' own snapping does. RegisterHotKey can't take keys Windows
// already owns, so this needs a low-level keyboard hook. The handler (run on the UI thread) says
// whether it moved something; if not, the key goes through and Windows does its usual thing.
sealed class ArrowKeyHook : IDisposable
{
    // Windows drops a hook that's too slow even once, so if the app is busy, Windows gets the key.
    static readonly TimeSpan AnswerWithin = TimeSpan.FromMilliseconds(150);

    readonly Func<Direction, bool> _handler;
    readonly LowLevelKeyboardProc _proc; // Held so the GC doesn't collect the callback.
    readonly HookThread _thread;

    public ArrowKeyHook(Func<Direction, bool> handler)
    {
        _handler = handler;
        _proc = OnKey;
        _thread = new HookThread("Win+arrow hook", () => SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0));
    }

    public bool IsInstalled => _thread.IsInstalled;

    // Every key on the system passes through here, so anything that isn't Win+arrow leaves at once.
    IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (ToDirection(key.vkCode) is { } direction && (key.flags & LLKHF_INJECTED) == 0 && OnlyWinHeld() && Ask(direction))
            {
                SwallowWin();
                return 1;
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    bool Ask(Direction direction)
    {
        try
        {
            return Dispatcher.UIThread.Invoke(() => _handler(direction), DispatcherPriority.Send, CancellationToken.None, AnswerWithin);
        }
        catch (TimeoutException)
        {
            return false;
        }
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

    public void Dispose() => _thread.Dispose();
}
