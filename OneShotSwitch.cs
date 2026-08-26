using MonitorTVSwitcher.Models;
using MonitorTVSwitcher.Services;

namespace MonitorTVSwitcher;

internal static class OneShotSwitch
{
    public static void Run(string targetName)
    {
        var settings = SettingsStore.Load();
        var displays = DisplayService.Enumerate();
        AppLog.Write("One-shot displays: " + string.Join(" | ", displays.Select(d =>
            $"{d.FriendlyName} role={DisplayClassifier.Resolve(d, settings)} active={d.IsActive} primary={d.IsPrimary}")));

        DisplayInfo? target = targetName.Equals("monitor", StringComparison.OrdinalIgnoreCase)
            ? displays.FirstOrDefault(d => DisplayClassifier.Resolve(d, settings) == DisplayRole.Monitor)
            : displays.FirstOrDefault(d => DisplayClassifier.Resolve(d, settings) == DisplayRole.Tv);

        if (target is null)
        {
            AppLog.Write($"One-shot could not find target '{targetName}'.");
            return;
        }

        bool persist = DisplayClassifier.Resolve(target, settings) == DisplayRole.Monitor;
        AppLog.Write($"One-shot switching to {target.MenuLabel} persist={persist}");
        DisplayService.SetExclusive(target, persist);
        AppLog.Write("One-shot switch finished.");
    }
}
