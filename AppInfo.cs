namespace MonitorTVSwitcher;

internal static class AppInfo
{
    public const string DisplayName = "Monitor/TV Switcher";
    public const string AppDataFolderName = "MonitorTVSwitcher";
    public const string LegacyAppDataFolderName = "GameShortcut";

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDataFolderName);

    public static void EnsureAppDataMigrated()
    {
        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LegacyAppDataFolderName);

        if (Directory.Exists(AppDataDirectory) || !Directory.Exists(legacy))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppDataDirectory)!);
            Directory.Move(legacy, AppDataDirectory);
        }
        catch
        {
            // Loaders fall back to defaults if the old folder cannot be moved.
        }
    }
}
