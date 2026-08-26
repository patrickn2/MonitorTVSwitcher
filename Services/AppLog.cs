namespace MonitorTVSwitcher.Services;

internal static class AppLog
{
    public static string FilePath { get; } = Path.Combine(AppInfo.AppDataDirectory, "switch.log");

    public static void Write(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break switching.
        }
    }
}
