using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Div.Core;
using static Div.Native;

namespace Div;

// Maximize-into-zone, done by the apps themselves. Each managed window gets tagged with the zone
// maximizing should fill, and DivHook.dll (native/) is loaded into its app. Inside, it
// turns the maximize command into a move into the zone (and back again next time); see the C file.
//
// The DLL only goes into apps whose windows we manage: never everything, never games.
sealed class ZoneMaximizer : IDisposable
{
    const string Property = "Div.MaximizeZone";
    const string OwnSizeProperty = "Div.OwnSize";
    const string DllName = "DivHook.dll";

    readonly IntPtr _dll, _proc, _cbtProc;
    // One hook per app thread with windows we've tagged, and the process it belonged to, so a
    // reused thread id isn't mistaken for one we've already hooked.
    readonly Dictionary<uint, (uint Process, IntPtr Hook, IntPtr Cbt)> _hooks = [];
    readonly HashSet<IntPtr> _tagged = [];

    public ZoneMaximizer()
    {
        if (LoadableCopy() is { } path) _dll = LoadLibrary(path);
        if (_dll != IntPtr.Zero) _proc = GetProcAddress(_dll, "DivCallWndRetProc");
        if (_dll != IntPtr.Zero) _cbtProc = GetProcAddress(_dll, "DivCbtProc");
        if (_proc == IntPtr.Zero) Log.Write($"Couldn't load {DllName}; maximizing will use the whole screen.");
    }

    // Apps keep the DLL loaded after we've gone, until their next message, which would lock the file
    // and stop a reinstall or rebuild. So apps get a copy named after its contents instead: a new
    // version is a new name, and the shipped file is never in use.
    static string? LoadableCopy()
    {
        try
        {
            var shipped = Path.Combine(AppContext.BaseDirectory, DllName);
            var bytes = File.ReadAllBytes(shipped);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..12];
            var copy = Path.Combine(AppPaths.LocalFolder, $"DivHook-{hash}.dll");
            if (!File.Exists(copy))
            {
                Directory.CreateDirectory(AppPaths.LocalFolder);
                File.WriteAllBytes(copy, bytes);
            }
            AllowStoreApps(copy);
            RemoveOldCopies(copy);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Couldn't prepare {DllName}: {ex.Message}");
            return null;
        }
    }

    // Each new version of the helper leaves its copy behind. Ones still pinned inside a running app
    // can't be deleted until that app closes; they're skipped and go next time.
    static void RemoveOldCopies(string current)
    {
        foreach (var old in Directory.GetFiles(AppPaths.LocalFolder, "DivHook-*.dll"))
        {
            if (string.Equals(old, current, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // Store apps (WhatsApp, for one) run sandboxed and can't read files in the user's folders unless
    // the file says so; without this, Windows quietly refuses to load the helper into them.
    static void AllowStoreApps(string path)
    {
        var file = new FileInfo(path);
        var acl = file.GetAccessControl();
        // ALL APPLICATION PACKAGES, and ALL RESTRICTED APPLICATION PACKAGES.
        foreach (var sid in new[] { "S-1-15-2-1", "S-1-15-2-2" })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        file.SetAccessControl(acl);
    }

    // Null zone: untag, so it maximizes to the whole screen as usual. ownSize: the window's size from
    // before it was snapped into a zone, for when maximize takes it back out of one.
    public void Tag(IntPtr hwnd, Rect? zone, (int Width, int Height)? ownSize = null)
    {
        if (zone is not { } z || _proc == IntPtr.Zero || !CanHelp(hwnd) || !Hook(hwnd))
        {
            Untag(hwnd);
            return;
        }
        SetProp(hwnd, Property, Pack(z));
        if (ownSize is { } size) SetProp(hwnd, OwnSizeProperty, (IntPtr)((size.Width & 0xFFFF) | (size.Height & 0xFFFF) << 16));
        else RemoveProp(hwnd, OwnSizeProperty);
        // The helper sets itself up in a tagged window when it sees the window's next message.
        // Send it one now (a no-op), so that's done before the first maximize, mouse or not.
        if (_tagged.Add(hwnd)) SendNotifyMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
    }

    public void Untag(IntPtr hwnd)
    {
        if (!_tagged.Remove(hwnd)) return;
        RemoveProp(hwnd, Property);
        RemoveProp(hwnd, OwnSizeProperty);
    }

    // The window's gone; its properties went with it.
    public void Forget(IntPtr hwnd) => _tagged.Remove(hwnd);

    // Four 16-bit screen coordinates in one value; the DLL unpacks them the same way.
    static IntPtr Pack(Rect zone) => (IntPtr)(long)(
        (ulong)(ushort)zone.Left | (ulong)(ushort)zone.Top << 16 | (ulong)(ushort)zone.Right << 32 | (ulong)(ushort)zone.Bottom << 48);

    // The DLL can only go into 64-bit apps (we're 64-bit). And DPI-unaware apps on a scaled monitor
    // see made-up coordinates, which would put the zone in the wrong place, so they're left alone.
    static bool CanHelp(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!IsWow64Process(process, out bool is32Bit) || is32Bit) return false;
        }
        finally
        {
            CloseHandle(process);
        }

        bool unaware = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd)) == DPI_AWARENESS_UNAWARE;
        bool scaled = GetDpiForMonitor(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi != 96;
        return !(unaware && scaled);
    }

    bool Hook(IntPtr hwnd)
    {
        uint thread = GetWindowThreadProcessId(hwnd, out uint pid);
        if (_hooks.TryGetValue(thread, out var existing) && existing.Process == pid) return true;

        var hook = SetWindowsHookEx(WH_CALLWNDPROCRET, _proc, _dll, thread);
        if (hook == IntPtr.Zero)
        {
            Log.Write($"Couldn't load the maximize helper into {WindowOps.GetProcessName(hwnd)} (error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        // Catches apps that maximize themselves instead of sending the maximize command. Without it
        // they still maximize into the zone, just as a real maximize.
        var cbt = _cbtProc != IntPtr.Zero ? SetWindowsHookEx(WH_CBT, _cbtProc, _dll, thread) : IntPtr.Zero;
        _hooks[thread] = (pid, hook, cbt);
        Log.Write($"Loaded the maximize helper into {WindowOps.GetProcessName(hwnd)}.");
        return true;
    }

    public void Dispose()
    {
        foreach (var hwnd in _tagged)
        {
            RemoveProp(hwnd, Property);
            RemoveProp(hwnd, OwnSizeProperty);
        }
        _tagged.Clear();
        // The DLL stays in each app until its next message, then Windows unloads it.
        foreach (var (_, hook, cbt) in _hooks.Values)
        {
            UnhookWindowsHookEx(hook);
            if (cbt != IntPtr.Zero) UnhookWindowsHookEx(cbt);
        }
        _hooks.Clear();
    }
}
