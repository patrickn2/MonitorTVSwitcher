using MonitorTVSwitcher.Models;
using MonitorTVSwitcher.Native;
using MonitorTVSwitcher.Services;
using Xunit;

namespace MonitorTVSwitcher.Tests;

public class AudioServiceTests
{
    [Fact]
    public void GetPlaybackDevices_returns_active_endpoints()
    {
        var devices = AudioService.GetPlaybackDevices();

        Assert.NotEmpty(devices);
        Assert.All(devices, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Id));
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
        });
    }

    [Fact]
    public void GuessPlaybackDeviceId_matches_tv_name_not_generic_digital_output()
    {
        var devices = new AudioDeviceInfo[]
        {
            new() { Id = "digital", Name = "Realtek Digital Output" },
            new() { Id = "tv", Name = "LG TV SSCR2" },
            new() { Id = "headset", Name = "Speakers (G733 Gaming Headset)" }
        };

        string? id = AudioService.GuessPlaybackDeviceId(Display("LG TV SSCR2"), devices);

        Assert.Equal("tv", id);
    }

    [Fact]
    public void GuessPlaybackDeviceId_prefers_the_target_display_over_another_lg_panel()
    {
        var devices = new AudioDeviceInfo[]
        {
            new() { Id = "monitor", Name = "LG ULTRAGEAR+ (NVIDIA High Definition Audio)" },
            new() { Id = "tv", Name = "LG TV SSCR2 (NVIDIA High Definition Audio)" }
        };

        string? id = AudioService.GuessPlaybackDeviceId(Display("LG TV SSCR2"), devices);

        Assert.Equal("tv", id);
    }

    private static DisplayInfo Display(string name) => new()
    {
        AdapterId = default,
        TargetId = 0,
        SourceId = 0,
        DevicePath = @"\\?\DISPLAY#TEST",
        FriendlyName = name,
        GdiDeviceName = @"\\.\DISPLAY1",
        OutputTechnology = OutputTechnology.Hdmi,
        IsActive = true,
        IsPrimary = true,
        IsAvailable = true
    };
}
