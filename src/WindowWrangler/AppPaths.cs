namespace WindowWrangler;

static class AppPaths
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowWrangler");

    public static string SettingsFile => Path.Combine(Folder, "settings.json");
    public static string LogFile => Path.Combine(Folder, "wrangler.log");
    public static string HistoryFile => Path.Combine(Folder, "history.json");

    // Machine-specific bits that shouldn't roam with the profile (the native helper's copies).
    public static string LocalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowWrangler");
}
