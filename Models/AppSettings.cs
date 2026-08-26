namespace MonitorTVSwitcher.Models;

public sealed class AppSettings
{
    public TriggerKind TriggerKind { get; set; } = TriggerKind.Chord;
    public List<int> Buttons { get; set; } = [];
    public bool StartWithWindows { get; set; }
    public string? LastMonitorPlaybackDeviceId { get; set; }
    public List<DisplayOverride> Displays { get; set; } = [];

    public DisplayOverride GetOrCreateOverride(string devicePath)
    {
        var existing = Displays.FirstOrDefault(d =>
            string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        existing = new DisplayOverride { DevicePath = devicePath };
        Displays.Add(existing);
        return existing;
    }
}
