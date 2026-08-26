using Microsoft.Win32;

namespace MonitorTVSwitcher.Services;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonitorTVSwitcher";
    private const string LegacyValueName = "GameShortcut";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string
            || key?.GetValue(LegacyValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the application path.");
        key.SetValue(ValueName, $"\"{path}\"");
    }
}
