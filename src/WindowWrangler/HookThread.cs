using static WindowWrangler.Native;

namespace WindowWrangler;

// Runs a low-level hook on a thread of its own. Windows calls these hooks on the thread that
// installed them, and every key press on the system waits for the answer. On the UI thread
// anything slow (a redraw, a dialog) would lag typing; this thread is always idle, so the hook
// answers straight away.
sealed class HookThread : IDisposable
{
    readonly Thread _thread;
    readonly ManualResetEventSlim _ready = new();
    uint _threadId;
    IntPtr _hook;

    // install runs on the new thread and returns the hook handle (zero if it failed).
    public HookThread(string name, Func<IntPtr> install)
    {
        _thread = new Thread(() => Run(install)) { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait();
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    void Run(Func<IntPtr> install)
    {
        _threadId = GetCurrentThreadId();
        _hook = install();
        _ready.Set();
        if (_hook == IntPtr.Zero) return;

        // The hook is called from inside GetMessage; nothing else ever arrives here but WM_QUIT.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            DispatchMessage(ref msg);
        UnhookWindowsHookEx(_hook);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _hook = IntPtr.Zero;
    }
}
