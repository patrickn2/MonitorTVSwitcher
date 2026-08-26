namespace MonitorTVSwitcher.Models;

public sealed class DisplayOverride
{
    public string DevicePath { get; set; } = "";
    public DisplayRole Role { get; set; } = DisplayRole.Auto;
    public string? PlaybackDeviceId { get; set; }
}
