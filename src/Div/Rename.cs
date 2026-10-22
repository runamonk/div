using System.ComponentModel;
using System.Diagnostics;

namespace Div;

// Div used to be called WindowWrangler. This moves an existing setup over the first time Div runs;
// once nobody has the old name around any more, this file can go.
static class Rename
{
    const string OldName = "WindowWrangler";

    // Before anything else reads or writes settings: close the old app (two copies would fight over
    // every window, and it would keep writing to the old folder), then move its folder over.
    public static void MoveIn()
    {
        var closed = StopOldApp();
        MoveFolder();
        RemoveOldHelpers();
        if (closed) Log.Write("Closed the old WindowWrangler that was still running.");
    }

    // The old name's copies of the maximize helper: some ended up in the settings folder (so got moved
    // along with it), the rest in the old local folder. Ones still loaded in a running app can't be
    // deleted until that app closes; they're skipped and go next time.
    static void RemoveOldHelpers()
    {
        var oldLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), OldName);
        foreach (var folder in new[] { AppPaths.Folder, oldLocal })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder, "WindowWranglerHook-*.dll"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        try
        {
            if (Directory.Exists(oldLocal) && Directory.GetFileSystemEntries(oldLocal).Length == 0) Directory.Delete(oldLocal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static bool StopOldApp()
    {
        bool closed = false;
        foreach (var old in Process.GetProcessesByName(OldName))
        {
            try
            {
                old.Kill();
                old.WaitForExit(3000);
                closed = true;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Probably running as admin while we aren't. The admin copy of Div will try again.
            }
        }
        return closed;
    }

    static void MoveFolder()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldName);
        if (!Directory.Exists(old) || Directory.Exists(AppPaths.Folder)) return;
        try
        {
            Directory.Move(old, AppPaths.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something has a file open in there; copy what matters instead.
            Directory.CreateDirectory(AppPaths.Folder);
            foreach (var name in new[] { "settings.json", "history.json" })
                if (File.Exists(Path.Combine(old, name))) File.Copy(Path.Combine(old, name), Path.Combine(AppPaths.Folder, name));
        }
        var oldLog = Path.Combine(AppPaths.Folder, "wrangler.log");
        if (File.Exists(oldLog) && !File.Exists(AppPaths.LogFile)) File.Move(oldLog, AppPaths.LogFile);
    }

    // Swap the old name's start-with-Windows entries for Div's own (the old task needs admin to remove).
    public static void MoveStartup(bool asAdmin)
    {
        try
        {
            if (Startup.RemoveOldName()) Startup.Set(true, asAdmin);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            Log.Write($"Couldn't move start with Windows over from WindowWrangler: {ex.Message}");
        }
    }
}
