namespace MonitorTVSwitcher.Models;

public sealed class AppSettings
{
    public TriggerKind TriggerKind { get; set; } = TriggerKind.Chord;
    public List<int> Buttons { get; set; } = [];
    public TriggerKind MouseTriggerKind { get; set; } = TriggerKind.Chord;
    public List<int> MouseButtons { get; set; } = [];
    public int MouseLeftButton { get; set; } = 0;
    public int MouseRightButton { get; set; } = 1;
    public int MouseSpeed { get; set; } = 50;
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
