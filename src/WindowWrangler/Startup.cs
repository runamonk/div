using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WindowWrangler;

// "Start with Windows". Normally the per-user Run key. When running as admin it's a scheduled task
// set to run with highest privileges instead: Windows won't start an admin app from the Run key
// without asking (it quietly skips it at logon), but a task like that starts with no prompt.
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WindowWrangler";
    const string TaskName = "WindowWrangler";

    // The real exe, even when launched through `dotnet WindowWrangler.dll` from the debugger.
    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "WindowWrangler.exe");

    public static bool IsEnabled => RunKeySet || TaskExists;

    // asAdmin needs us to be running as admin right now: only then can the task be created.
    public static void Set(bool enabled, bool asAdmin)
    {
        if (!enabled)
        {
            RemoveRunKey();
            DeleteTask();
        }
        else if (asAdmin)
        {
            CreateTask();
            RemoveRunKey();
        }
        else
        {
            SetRunKey();
            DeleteTask();
        }
    }

    // If it's on, make sure it's the right kind for the mode we're in.
    public static void Match(bool asAdmin)
    {
        if (IsEnabled && (asAdmin ? !TaskExists || RunKeySet : !RunKeySet || TaskExists)) Set(true, asAdmin);
    }

    static bool RunKeySet
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    static void SetRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{ExePath}\"");
    }

    static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    static bool TaskExists => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    static void DeleteTask()
    {
        if (TaskExists && Schtasks($"/Delete /TN \"{TaskName}\" /F") != 0)
            Log.Write("Couldn't remove the start-with-Windows task (it needs admin to remove).");
    }

    // Written out in full rather than with schtasks' shortcuts: those default to stopping the app
    // after 72 hours and not starting it on battery, neither of which suits something in the tray.
    static void CreateTask()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(ExePath);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Window Wrangler as administrator when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"WindowWrangler-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            if (Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F") != 0)
                throw new InvalidOperationException("Windows wouldn't create the start-with-Windows task; that needs Window Wrangler running as admin.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    static int Schtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
