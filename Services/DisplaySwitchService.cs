using MonitorTVSwitcher.Models;

namespace MonitorTVSwitcher.Services;

internal sealed class DisplaySwitchService
{
    private int _busy;
    private readonly JoystickWatcher _joystick;

    public DisplaySwitchService(JoystickWatcher joystick)
    {
        _joystick = joystick;
    }

    public event Action? BusyChanged;
    public event Action<string>? StatusChanged;
    public event Action<DisplayRole>? ExclusiveRoleChanged;

    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public string LastStatus { get; private set; } = "Ready.";

    public void ToggleFromJoystick()
    {
        var displays = DisplayService.Enumerate();
        var current = DisplayService.GetCurrent(displays);
        var settings = SettingsStore.Load();
        var target = PickOpposite(displays, current, settings);
        if (target is null)
        {
            SetStatus("No other display is available.");
            return;
        }

        AppLog.Write($"Joystick display toggle → {target.MenuLabel}");
        SwitchTo(target.DevicePath);
    }

    public void SwitchTo(string devicePath)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            AppLog.Write("Switch ignored: already busy.");
            return;
        }

        _joystick.IsBusy = true;
        _joystick.ResetSequence();
        BusyChanged?.Invoke();
        SetStatus("Switching display…");
        AppLog.Write($"SwitchTo start {devicePath}");

        try
        {
            PerformSwitch(devicePath);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Switch failed: {ex}");
            SetStatus($"Switch failed: {ex.Message}");
        }
        finally
        {
            EndBusy();
        }
    }

    private void EndBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        _joystick.IsBusy = false;
        _joystick.ResetSequence();
        BusyChanged?.Invoke();
        AppLog.Write("Switch pipeline finished; commands accepted again.");
    }

    private void PerformSwitch(string devicePath)
    {
        var settings = SettingsStore.Load();
        var displays = DisplayService.Enumerate();
        var current = DisplayService.GetCurrent(displays);
        var target = displays.FirstOrDefault(d =>
            string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            SetStatus("The selected display is not connected.");
            return;
        }

        if (current is not null
            && string.Equals(current.DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase)
            && displays.Count(d => d.IsActive) == 1)
        {
            AppLog.Write($"Already exclusive on {target.MenuLabel}; not re-applying mode/HDR.");
            SetStatus($"Already on {target.MenuLabel}.");
            return;
        }

        bool leavingMonitor = current is not null
            && DisplayClassifier.Resolve(current, settings) == DisplayRole.Monitor;

        if (leavingMonitor)
        {
            settings.LastMonitorPlaybackDeviceId = AudioService.GetDefaultPlaybackId();
        }

        DisplayService.SetExclusive(target, persist: DisplayClassifier.Resolve(target, settings) == DisplayRole.Monitor);
        AppLog.Write("Waiting for the display to settle.");
        Thread.Sleep(1500);

        var refreshed = DisplayService.Enumerate().FirstOrDefault(d =>
            string.Equals(d.DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase)) ?? target;

        ApplyModeHdrAndAudio(refreshed, settings);
        SettingsStore.Save(settings);
        ExclusiveRoleChanged?.Invoke(DisplayClassifier.Resolve(refreshed, settings));
        SetStatus($"Switched to {refreshed.MenuLabel}.");
    }

    private static void ApplyModeHdrAndAudio(DisplayInfo target, AppSettings settings)
    {
        try
        {
            DisplayService.ApplyBestMode(target);
            Thread.Sleep(1000);
        }
        catch (Exception ex)
        {
            AppLog.Write($"ApplyBestMode failed: {ex.Message}");
        }

        try
        {
            DisplayService.EnableHdr(target);
        }
        catch (Exception ex)
        {
            AppLog.Write($"EnableHdr failed: {ex.Message}");
        }

        try
        {
            ApplyAudio(target, settings);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Audio switch failed: {ex.Message}");
        }
    }

    private static void ApplyAudio(DisplayInfo target, AppSettings settings)
    {
        var role = DisplayClassifier.Resolve(target, settings);
        var devices = AudioService.GetPlaybackDevices();
        var mapping = settings.GetOrCreateOverride(target.DevicePath);

        if (role == DisplayRole.Tv)
        {
            string? audioId = mapping.PlaybackDeviceId
                ?? AudioService.GuessPlaybackDeviceId(target, devices);
            if (!string.IsNullOrWhiteSpace(audioId))
            {
                AudioService.SetDefaultPlayback(audioId);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.LastMonitorPlaybackDeviceId))
        {
            AudioService.SetDefaultPlayback(settings.LastMonitorPlaybackDeviceId);
        }
        else if (!string.IsNullOrWhiteSpace(mapping.PlaybackDeviceId))
        {
            AudioService.SetDefaultPlayback(mapping.PlaybackDeviceId);
        }
    }

    private static DisplayInfo? PickOpposite(IReadOnlyList<DisplayInfo> displays, DisplayInfo? current, AppSettings settings)
    {
        if (displays.Count == 0)
        {
            return null;
        }

        if (current is null)
        {
            return displays.FirstOrDefault();
        }

        var currentRole = DisplayClassifier.Resolve(current, settings);
        var oppositeRole = currentRole == DisplayRole.Tv ? DisplayRole.Monitor : DisplayRole.Tv;

        return displays.FirstOrDefault(d =>
                   !string.Equals(d.DevicePath, current.DevicePath, StringComparison.OrdinalIgnoreCase)
                   && DisplayClassifier.Resolve(d, settings) == oppositeRole)
            ?? displays.FirstOrDefault(d =>
                !string.Equals(d.DevicePath, current.DevicePath, StringComparison.OrdinalIgnoreCase));
    }

    private void SetStatus(string status)
    {
        LastStatus = status;
        StatusChanged?.Invoke(status);
    }
}
