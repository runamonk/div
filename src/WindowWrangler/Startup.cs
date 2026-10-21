using Microsoft.Win32;

namespace WindowWrangler;

// "Start with Windows" via the per-user Run key. No admin needed.
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WindowWrangler";

    // The real exe, even when launched through `dotnet WindowWrangler.dll` from the debugger.
    static string ExePath => Path.Combine(AppContext.BaseDirectory, "WindowWrangler.exe");

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{ExePath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
