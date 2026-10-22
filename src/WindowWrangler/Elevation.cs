using System.ComponentModel;
using System.Diagnostics;

namespace WindowWrangler;

// Running as admin, so apps that run as admin can be managed too.
static class Elevation
{
    // Passed to a copy we start ourselves, so it waits for this one to finish closing.
    public const string RestartedArg = "--restarted";

    public static bool IsAdmin => Environment.IsPrivilegedProcess;

    // Starts another copy as admin; Windows asks first. False if the answer was no.
    public static bool RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Startup.ExePath, RestartedArg) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return false;
        }
    }
}
