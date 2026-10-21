namespace WindowWrangler;

// Plain text log so you can see what got moved and why.
static class Log
{
    const long MaxBytes = 1024 * 1024;

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Folder);
            var file = new FileInfo(AppPaths.LogFile);
            if (file.Exists && file.Length > MaxBytes) file.Delete();
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
