using System.Runtime.InteropServices;
using GameShortcut.Models;
using GameShortcut.Native;

namespace GameShortcut.Services;

internal static class AudioService
{
    public static IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)CoreAudioInterop.CreateEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.Render, CoreAudioInterop.DeviceStateActive, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));

            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IPropertyStore? store = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out store));

                    var key = CoreAudioInterop.PkeyDeviceFriendlyName;
                    Marshal.ThrowExceptionForHR(store.GetValue(ref key, out PropVariant value));
                    string? name = null;
                    try
                    {
                        if ((value.vt == CoreAudioInterop.VtLpWStr || value.vt == 8) && value.Data != IntPtr.Zero)
                        {
                            name = Marshal.PtrToStringUni(value.Data);
                        }
                    }
                    finally
                    {
                        CoreAudioInterop.PropVariantClear(ref value);
                    }

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    {
                        devices.Add(new AudioDeviceInfo { Id = id, Name = name });
                    }
                }
                finally
                {
                    if (store is not null)
                    {
                        Marshal.ReleaseComObject(store);
                    }

                    if (device is not null)
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
        }
        catch
        {
            return devices;
        }
        finally
        {
            if (collection is not null)
            {
                Marshal.ReleaseComObject(collection);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string? GetDefaultPlaybackId()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)CoreAudioInterop.CreateEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
            Marshal.ThrowExceptionForHR(device.GetId(out string id));
            return id;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    public static void SetDefaultPlayback(string deviceId)
    {
        object client = new PolicyConfigClient();
        try
        {
            if (client is IPolicyConfig config)
            {
                ThrowIfFailed(config.SetDefaultEndpoint(deviceId, ERole.Console));
                ThrowIfFailed(config.SetDefaultEndpoint(deviceId, ERole.Multimedia));
                ThrowIfFailed(config.SetDefaultEndpoint(deviceId, ERole.Communications));
                return;
            }

            if (client is IPolicyConfigWin10 win10)
            {
                ThrowIfFailed(win10.SetDefaultEndpoint(deviceId, ERole.Console));
                ThrowIfFailed(win10.SetDefaultEndpoint(deviceId, ERole.Multimedia));
                ThrowIfFailed(win10.SetDefaultEndpoint(deviceId, ERole.Communications));
                return;
            }

            if (client is IPolicyConfigVista vista)
            {
                ThrowIfFailed(vista.SetDefaultEndpoint(deviceId, ERole.Console));
                ThrowIfFailed(vista.SetDefaultEndpoint(deviceId, ERole.Multimedia));
                ThrowIfFailed(vista.SetDefaultEndpoint(deviceId, ERole.Communications));
                return;
            }

            throw new InvalidOperationException("Could not access the Windows audio policy interface.");
        }
        finally
        {
            Marshal.FinalReleaseComObject(client);
        }
    }

    public static string? GuessPlaybackDeviceId(DisplayInfo display, IReadOnlyList<AudioDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            return null;
        }

        string displayName = display.FriendlyName;
        var byName = devices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(displayName)
            && d.Name.Contains(FirstToken(displayName), StringComparison.OrdinalIgnoreCase)
            && LooksLikeDisplayAudio(d.Name));
        if (byName is not null)
        {
            return byName.Id;
        }

        var hdmi = devices.FirstOrDefault(d => LooksLikeDisplayAudio(d.Name));
        return hdmi?.Id;
    }

    private static bool LooksLikeDisplayAudio(string name)
    {
        return name.Contains("HDMI", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Display", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Digital", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ARC", StringComparison.OrdinalIgnoreCase)
            || name.Contains("eARC", StringComparison.OrdinalIgnoreCase)
            || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AMD HDMI", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Intel(R) Display", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstToken(string name)
    {
        var parts = name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? name : parts[0];
    }

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
