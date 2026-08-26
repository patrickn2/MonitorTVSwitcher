using MonitorTVSwitcher.Native;

namespace MonitorTVSwitcher.Models;

internal sealed class DisplayInfo
{
    public required Luid AdapterId { get; init; }
    public required uint TargetId { get; init; }
    public required uint SourceId { get; init; }
    public required string DevicePath { get; init; }
    public required string FriendlyName { get; init; }
    public required string GdiDeviceName { get; init; }
    public required OutputTechnology OutputTechnology { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsPrimary { get; init; }
    public required bool IsAvailable { get; init; }

    public string MenuLabel =>
        string.IsNullOrWhiteSpace(FriendlyName) ? GdiDeviceName : FriendlyName;
}
