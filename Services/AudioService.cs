using System.Runtime.InteropServices;
using MonitorTVSwitcher.Models;
using MonitorTVSwitcher.Native;

namespace MonitorTVSwitcher.Services;

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
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.Render, CoreAudioInterop.DeviceStateActive, out IntPtr collectionPtr));
            collection = WrapDeviceCollection(collectionPtr);
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
                    string? name = ReadDeviceName(store);

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    {
                        devices.Add(new AudioDeviceInfo { Id = id, Name = name });
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Skipping audio endpoint: {ex.Message}");
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
        catch (Exception ex)
        {
            AppLog.Write($"GetPlaybackDevices failed: {ex.Message}");
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

        string displayName = display.FriendlyName?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var byDisplayName = devices.FirstOrDefault(d =>
                d.Name.Contains(displayName, StringComparison.OrdinalIgnoreCase));
            if (byDisplayName is not null)
            {
                return byDisplayName.Id;
            }

            string token = FirstToken(displayName);
            var byName = devices.FirstOrDefault(d =>
                d.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
                && LooksLikeDisplayAudio(d.Name));
            if (byName is not null)
            {
                return byName.Id;
            }
        }

        var hdmi = devices.FirstOrDefault(d => LooksLikeDisplayAudio(d.Name));
        return hdmi?.Id;
    }

    private static IMMDeviceCollection WrapDeviceCollection(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("EnumAudioEndpoints returned no collection.");
        }

        try
        {
            return (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static string? ReadDeviceName(IPropertyStore store)
    {
        var friendly = CoreAudioInterop.PkeyDeviceFriendlyName;
        string? name = ReadPropString(store, ref friendly);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var desc = CoreAudioInterop.PkeyDeviceDeviceDesc;
        return ReadPropString(store, ref desc);
    }

    private static string? ReadPropString(IPropertyStore store, ref PropertyKey key)
    {
        int hr = store.GetValue(ref key, out PropVariant value);
        try
        {
            if (hr < 0)
            {
                return null;
            }

            if ((value.vt == CoreAudioInterop.VtLpWStr || value.vt == 8) && value.Data != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(value.Data);
            }

            return null;
        }
        finally
        {
            CoreAudioInterop.PropVariantClear(ref value);
        }
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
