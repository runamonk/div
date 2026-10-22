using System.Runtime.InteropServices;
using static Div.Native;

namespace Div;

// A system-wide hotkey. RegisterHotKey wants a window to send WM_HOTKEY to, and we
// have no main window, so this makes a hidden message-only one.
sealed class Hotkey : IDisposable
{
    const string ClassName = "DivHotkey";
    const int Id = 1;

    static WndProc? _classProc;
    static readonly Dictionary<IntPtr, Hotkey> Instances = [];

    readonly Action _pressed;
    IntPtr _hwnd;

    public bool IsRegistered { get; }

    public Hotkey(uint modifiers, uint key, Action pressed)
    {
        _pressed = pressed;
        if (_classProc is null)
        {
            _classProc = Proc; // Kept alive for as long as the class exists.
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _classProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = ClassName,
            };
            RegisterClassEx(ref wc);
        }

        _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        Instances[_hwnd] = this;
        // Fails if another app already owns the combination.
        IsRegistered = RegisterHotKey(_hwnd, Id, modifiers | MOD_NOREPEAT, key);
    }

    static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && Instances.TryGetValue(hwnd, out var hotkey))
        {
            hotkey._pressed();
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, Id);
        Instances.Remove(_hwnd);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
