using MonitorTVSwitcher.Models;
using MonitorTVSwitcher.Native;

namespace MonitorTVSwitcher.Services;

internal static class DisplayService
{
    public static IReadOnlyList<DisplayInfo> Enumerate()
    {
        if (!TryQuery(CcdInterop.QdcAllPaths | CcdInterop.QdcVirtualModeAware, out var paths, out _))
        {
            return [];
        }

        var results = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (path.TargetInfo.TargetAvailable == 0)
            {
                continue;
            }

            var targetName = new DisplayConfigTargetDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = CcdInterop.DeviceInfoGetTargetName,
                    Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                    AdapterId = path.TargetInfo.AdapterId,
                    Id = path.TargetInfo.Id
                }
            };

            if (CcdInterop.GetDeviceInfo(ref targetName) != CcdInterop.ErrorSuccess)
            {
                continue;
            }

            string devicePath = targetName.MonitorDevicePath ?? "";
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                continue;
            }

            var sourceName = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = CcdInterop.DeviceInfoGetSourceName,
                    Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id
                }
            };

            string gdiName = "";
            if (CcdInterop.GetDeviceInfo(ref sourceName) == CcdInterop.ErrorSuccess)
            {
                gdiName = sourceName.ViewGdiDeviceName ?? "";
            }

            bool isActive = (path.Flags & CcdInterop.PathActive) != 0
                || (path.TargetInfo.StatusFlags & CcdInterop.TargetInUse) != 0;

            var info = new DisplayInfo
            {
                AdapterId = path.TargetInfo.AdapterId,
                TargetId = path.TargetInfo.Id,
                SourceId = path.SourceInfo.Id,
                DevicePath = devicePath,
                FriendlyName = string.IsNullOrWhiteSpace(targetName.MonitorFriendlyDeviceName)
                    ? (string.IsNullOrWhiteSpace(gdiName) ? "Display" : gdiName)
                    : targetName.MonitorFriendlyDeviceName,
                GdiDeviceName = gdiName,
                OutputTechnology = targetName.OutputTechnology,
                IsActive = isActive,
                IsPrimary = IsPrimary(gdiName),
                IsAvailable = true
            };

            if (results.TryGetValue(devicePath, out var existing) && existing.IsActive && !isActive)
            {
                continue;
            }

            results[devicePath] = info;
        }

        return results.Values
            .OrderByDescending(d => d.IsPrimary)
            .ThenByDescending(d => d.IsActive)
            .ThenBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static DisplayInfo? GetCurrent(IReadOnlyList<DisplayInfo> displays)
    {
        var active = displays.Where(d => d.IsActive).ToList();
        if (active.Count == 1)
        {
            return active[0];
        }

        return active.FirstOrDefault(d => d.IsPrimary)
            ?? active.FirstOrDefault()
            ?? displays.FirstOrDefault();
    }

    public static void SetExclusive(DisplayInfo target, bool persist)
    {
        AppLog.Write($"SetExclusive '{target.FriendlyName}' persist={persist} adapter={target.AdapterId.LowPart:X}/{target.AdapterId.HighPart} targetId={target.TargetId} gdi={target.GdiDeviceName}");
        LogTopology("before");

        var errors = new List<string>();

        int ccd = TryDisableOthersOnActiveTopology(target, errors, persist);
        if (ccd == CcdInterop.ErrorSuccess)
        {
            AppLog.Write("Exclusive switch applied via active-path rewrite. Not retrying.");
            LogTopology("after");
            return;
        }

        ccd = TryCcdSinglePath(target, errors, persist);
        if (ccd == CcdInterop.ErrorSuccess)
        {
            AppLog.Write("Exclusive switch applied via CCD single path. Not retrying.");
            LogTopology("after");
            return;
        }

        if (TryGdiExclusive(target, errors, persist))
        {
            AppLog.Write("Exclusive switch applied via ChangeDisplaySettingsEx. Not retrying.");
            LogTopology("after");
            return;
        }

        LogTopology("after-failure");
        throw new InvalidOperationException(
            $"Could not switch to {target.FriendlyName}. {string.Join(" ", errors)}");
    }

    private static int TryDisableOthersOnActiveTopology(DisplayInfo target, List<string> errors, bool persist)
    {
        if (!TryQuery(CcdInterop.QdcOnlyActivePaths, out var paths, out var modes))
        {
            errors.Add("Could not query active display paths.");
            return -1;
        }

        int matchIndex = FindPathIndex(paths, target, requireAvailable: false);
        if (matchIndex < 0)
        {
            AppLog.Write("Target is not in the active topology; skipping active-path rewrite.");
            return -1;
        }

        bool changed = false;
        for (int i = 0; i < paths.Length; i++)
        {
            uint desired = i == matchIndex ? CcdInterop.PathActive : 0u;
            if (paths[i].Flags != desired)
            {
                paths[i].Flags = desired;
                changed = true;
            }
        }

        if (!changed && paths.Length == 1)
        {
            AppLog.Write("Target is already the only active display.");
            return CcdInterop.ErrorSuccess;
        }

        uint flags = CcdInterop.SdcApply
            | CcdInterop.SdcUseSuppliedDisplayConfig
            | CcdInterop.SdcAllowChanges;
        if (persist)
        {
            flags |= CcdInterop.SdcSaveToDatabase;
        }

        int result = CcdInterop.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags);
        AppLog.Write($"Active-path rewrite SetDisplayConfig={result} paths={paths.Length} modes={modes.Length}");
        if (result != CcdInterop.ErrorSuccess)
        {
            errors.Add($"Active-path switch failed ({result}).");
        }

        return result;
    }

    private static int TryCcdSinglePath(DisplayInfo target, List<string> errors, bool persist)
    {
        if (!TryQuery(CcdInterop.QdcAllPaths, out var paths, out _))
        {
            errors.Add("Could not query all display paths.");
            return -1;
        }

        int matchIndex = FindPathIndex(paths, target, requireAvailable: true);
        if (matchIndex < 0)
        {
            errors.Add($"Display '{target.FriendlyName}' is not available.");
            return -1;
        }

        var selected = paths[matchIndex];
        selected.Flags = CcdInterop.PathActive;
        selected.SourceInfo.ModeInfoIdx = CcdInterop.PathModeIdxInvalid;
        selected.TargetInfo.ModeInfoIdx = CcdInterop.PathModeIdxInvalid;
        var single = new[] { selected };

        uint save = persist ? CcdInterop.SdcSaveToDatabase : 0;
        uint[] attempts =
        [
            CcdInterop.SdcApply | CcdInterop.SdcTopologySupplied | CcdInterop.SdcAllowChanges | save,
            CcdInterop.SdcApply | CcdInterop.SdcUseSuppliedDisplayConfig | CcdInterop.SdcAllowChanges | save
        ];

        int last = -1;
        foreach (uint flags in attempts)
        {
            last = CcdInterop.SetDisplayConfig(1, single, 0, null, flags);
            AppLog.Write($"Single-path SetDisplayConfig flags=0x{flags:X} result={last}");
            if (last == CcdInterop.ErrorSuccess)
            {
                return last;
            }

            errors.Add($"CCD 0x{flags:X}={last}.");
        }

        return last;
    }

    private static bool TryGdiExclusive(DisplayInfo target, List<string> errors, bool persist)
    {
        string? targetGdi = ResolveGdiName(target);
        if (string.IsNullOrWhiteSpace(targetGdi))
        {
            targetGdi = target.GdiDeviceName;
        }

        if (string.IsNullOrWhiteSpace(targetGdi))
        {
            errors.Add("Missing GDI device name for the target display.");
            return false;
        }

        if (!TryGetDevMode(targetGdi, out var mode))
        {
            errors.Add($"Could not read the current mode for {targetGdi}.");
            return false;
        }

        mode.dmPositionX = 0;
        mode.dmPositionY = 0;
        mode.dmFields = DisplayDeviceInterop.DmPosition
            | DisplayDeviceInterop.DmPelsWidth
            | DisplayDeviceInterop.DmPelsHeight
            | DisplayDeviceInterop.DmBitsPerPel
            | DisplayDeviceInterop.DmDisplayFrequency;

        uint pendingFlags = persist
            ? DisplayDeviceInterop.CdsUpdateRegistry | DisplayDeviceInterop.CdsNoReset
            : DisplayDeviceInterop.CdsNoReset;

        int primaryResult = DisplayDeviceInterop.ChangeDisplaySettingsEx(
            targetGdi,
            ref mode,
            IntPtr.Zero,
            pendingFlags | DisplayDeviceInterop.CdsSetPrimary,
            IntPtr.Zero);
        AppLog.Write($"GDI set primary {targetGdi} result={primaryResult} {mode.dmPelsWidth}x{mode.dmPelsHeight}@{mode.dmDisplayFrequency}");

        foreach (string other in GetAttachedGdiNames())
        {
            if (string.Equals(other, targetGdi, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var off = new DevMode
            {
                dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>(),
                dmPelsWidth = 0,
                dmPelsHeight = 0,
                dmPositionX = 0,
                dmPositionY = 0,
                dmFields = DisplayDeviceInterop.DmPosition
                    | DisplayDeviceInterop.DmPelsWidth
                    | DisplayDeviceInterop.DmPelsHeight
            };

            int detachResult = DisplayDeviceInterop.ChangeDisplaySettingsEx(
                other,
                ref off,
                IntPtr.Zero,
                pendingFlags,
                IntPtr.Zero);
            AppLog.Write($"GDI detach {other} result={detachResult}");
        }

        int applyResult = DisplayDeviceInterop.ChangeDisplaySettingsEx(
            null,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            IntPtr.Zero);
        AppLog.Write($"GDI apply result={applyResult}");
        return applyResult == DisplayDeviceInterop.DispChangeSuccessful;
    }

    private static int FindPathIndex(DisplayConfigPathInfo[] paths, DisplayInfo target, bool requireAvailable)
    {
        int fallback = -1;
        for (int i = 0; i < paths.Length; i++)
        {
            if (requireAvailable && paths[i].TargetInfo.TargetAvailable == 0)
            {
                continue;
            }

            bool sameTarget = paths[i].TargetInfo.AdapterId == target.AdapterId
                && paths[i].TargetInfo.Id == target.TargetId;
            if (!sameTarget)
            {
                string? path = GetDevicePath(paths[i]);
                sameTarget = !string.IsNullOrWhiteSpace(path)
                    && string.Equals(path, target.DevicePath, StringComparison.OrdinalIgnoreCase);
            }

            if (!sameTarget)
            {
                continue;
            }

            if ((paths[i].Flags & CcdInterop.PathActive) != 0)
            {
                return i;
            }

            if (fallback < 0)
            {
                fallback = i;
            }
        }

        return fallback;
    }

    private static string? GetDevicePath(DisplayConfigPathInfo path)
    {
        var targetName = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = CcdInterop.DeviceInfoGetTargetName,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id
            }
        };

        if (CcdInterop.GetDeviceInfo(ref targetName) != CcdInterop.ErrorSuccess)
        {
            return null;
        }

        return targetName.MonitorDevicePath;
    }

    private static bool IsNowCurrent(DisplayInfo target)
    {
        Thread.Sleep(400);
        var displays = Enumerate();
        var current = GetCurrent(displays);
        bool match = current is not null
            && string.Equals(current.DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase);
        AppLog.Write($"Verify current='{current?.FriendlyName}' active={displays.Count(d => d.IsActive)} match={match}");
        return match;
    }

    private static void LogTopology(string label)
    {
        try
        {
            var displays = Enumerate();
            AppLog.Write($"Topology {label}: " + string.Join(" | ", displays.Select(d =>
                $"{d.FriendlyName} active={d.IsActive} primary={d.IsPrimary} gdi={d.GdiDeviceName}")));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Topology {label} failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> GetAttachedGdiNames()
    {
        var names = new List<string>();
        var adapter = new DisplayDevice
        {
            cb = System.Runtime.InteropServices.Marshal.SizeOf<DisplayDevice>()
        };

        for (uint i = 0; DisplayDeviceInterop.EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & DisplayDeviceInterop.DisplayDeviceAttachedToDesktop) != 0
                && !string.IsNullOrWhiteSpace(adapter.DeviceName))
            {
                names.Add(adapter.DeviceName);
            }

            adapter.cb = System.Runtime.InteropServices.Marshal.SizeOf<DisplayDevice>();
        }

        return names;
    }

    private static bool TryGetDevMode(string gdiName, out DevMode mode)
    {
        foreach (int index in new[] { DisplayDeviceInterop.EnumCurrentSettings, DisplayDeviceInterop.EnumRegistrySettings, 0 })
        {
            mode = new DevMode
            {
                dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>()
            };

            if (DisplayDeviceInterop.EnumDisplaySettingsEx(gdiName, index, ref mode, 0)
                && mode.dmPelsWidth > 0
                && mode.dmPelsHeight > 0)
            {
                return true;
            }
        }

        mode = default;
        return false;
    }

    public static void ApplyBestMode(DisplayInfo display)
    {
        string gdiName = ResolveGdiName(display);
        if (string.IsNullOrWhiteSpace(gdiName))
        {
            AppLog.Write("ApplyBestMode skipped: no GDI name.");
            return;
        }

        if (!TryGetPreferredMode(display, out uint width, out uint height, out uint refresh)
            && !TryGetEnumeratedBestMode(gdiName, out width, out height, out refresh))
        {
            AppLog.Write("ApplyBestMode skipped: no preferred mode.");
            return;
        }

        if (!TryGetDevMode(gdiName, out var current)
            || current.dmPelsWidth != width
            || current.dmPelsHeight != height
            || current.dmDisplayFrequency != refresh)
        {
            var mode = new DevMode
            {
                dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>(),
                dmPelsWidth = width,
                dmPelsHeight = height,
                dmDisplayFrequency = refresh,
                dmBitsPerPel = current.dmBitsPerPel == 0 ? 32 : current.dmBitsPerPel,
                dmFields = DisplayDeviceInterop.DmPelsWidth
                    | DisplayDeviceInterop.DmPelsHeight
                    | DisplayDeviceInterop.DmDisplayFrequency
                    | DisplayDeviceInterop.DmBitsPerPel
            };

            int result = DisplayDeviceInterop.ChangeDisplaySettingsEx(
                gdiName,
                ref mode,
                IntPtr.Zero,
                DisplayDeviceInterop.CdsUpdateRegistry,
                IntPtr.Zero);
            AppLog.Write($"ApplyBestMode {gdiName} {width}x{height}@{refresh} result={result}");
            return;
        }

        AppLog.Write($"ApplyBestMode skipped: already {width}x{height}@{refresh}");
    }

    private static bool TryGetPreferredMode(DisplayInfo display, out uint width, out uint height, out uint refresh)
    {
        width = 0;
        height = 0;
        refresh = 0;

        var packet = new DisplayConfigTargetPreferredMode
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = CcdInterop.DeviceInfoGetTargetPreferredMode,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigTargetPreferredMode>(),
                AdapterId = display.AdapterId,
                Id = display.TargetId
            }
        };

        if (CcdInterop.GetDeviceInfo(ref packet) != CcdInterop.ErrorSuccess || packet.Width == 0 || packet.Height == 0)
        {
            return false;
        }

        width = packet.Width;
        height = packet.Height;
        var vsync = packet.TargetVideoSignalInfo.VSyncFreq;
        if (vsync.Denominator > 0)
        {
            refresh = (uint)Math.Round(vsync.Numerator / (double)vsync.Denominator);
        }

        if (refresh == 0)
        {
            refresh = 60;
        }

        AppLog.Write($"Preferred mode {width}x{height}@{refresh}");
        return true;
    }

    private static bool TryGetEnumeratedBestMode(string gdiName, out uint width, out uint height, out uint refresh)
    {
        width = 0;
        height = 0;
        refresh = 0;
        var best = FindBestMode(gdiName);
        if (best is null)
        {
            return false;
        }

        width = best.Value.dmPelsWidth;
        height = best.Value.dmPelsHeight;
        refresh = best.Value.dmDisplayFrequency;
        return width > 0 && height > 0;
    }

    public static void EnableHdr(DisplayInfo display)
    {
        if (!TryGetColorInfo(display, out uint value, out bool supported))
        {
            return;
        }

        if (!supported)
        {
            AppLog.Write("HDR not supported on this display.");
            return;
        }

        const uint advancedColorEnabled = 0x2;
        if ((value & advancedColorEnabled) != 0)
        {
            AppLog.Write("HDR already enabled.");
            return;
        }

        var hdr = new DisplayConfigSetHdrState
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = CcdInterop.DeviceInfoSetHdrState,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigSetHdrState>(),
                AdapterId = display.AdapterId,
                Id = display.TargetId
            },
            EnableHdr = 1
        };

        int hdrResult = CcdInterop.SetDeviceInfo(hdr);
        AppLog.Write($"EnableHdr SET_HDR_STATE={hdrResult}");
        if (hdrResult == CcdInterop.ErrorSuccess)
        {
            return;
        }

        var advanced = new DisplayConfigSetAdvancedColorState
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = CcdInterop.DeviceInfoSetAdvancedColorState,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigSetAdvancedColorState>(),
                AdapterId = display.AdapterId,
                Id = display.TargetId
            },
            EnableAdvancedColor = 1
        };

        int advancedResult = CcdInterop.SetDeviceInfo(advanced);
        AppLog.Write($"EnableHdr SET_ADVANCED_COLOR_STATE={advancedResult}");
    }

    private static bool TryGetColorInfo(DisplayInfo display, out uint value, out bool supported)
    {
        value = 0;
        supported = false;

        var info = new DisplayConfigGetAdvancedColorInfo
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = CcdInterop.DeviceInfoGetAdvancedColorInfo,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(),
                AdapterId = display.AdapterId,
                Id = display.TargetId
            }
        };

        if (CcdInterop.GetDeviceInfo(ref info) != CcdInterop.ErrorSuccess)
        {
            return false;
        }

        value = info.Value;
        supported = (info.Value & 0x1) != 0;
        return true;
    }

    private static string ResolveGdiName(DisplayInfo display)
    {
        var refreshed = Enumerate().FirstOrDefault(d =>
            string.Equals(d.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase));
        return refreshed?.GdiDeviceName ?? display.GdiDeviceName;
    }

    private static DevMode? FindBestMode(string gdiName)
    {
        DevMode best = default;
        bool found = false;
        int index = 0;

        while (true)
        {
            var mode = new DevMode
            {
                dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>()
            };

            if (!DisplayDeviceInterop.EnumDisplaySettingsEx(gdiName, index, ref mode, 0))
            {
                break;
            }

            index++;
            if (mode.dmPelsWidth == 0 || mode.dmPelsHeight == 0)
            {
                continue;
            }

            if ((mode.dmDisplayFlags & DisplayDeviceInterop.DisplayInterlaced) != 0)
            {
                continue;
            }

            if (!found
                || mode.dmPelsWidth * mode.dmPelsHeight > best.dmPelsWidth * best.dmPelsHeight
                || (mode.dmPelsWidth == best.dmPelsWidth
                    && mode.dmPelsHeight == best.dmPelsHeight
                    && mode.dmDisplayFrequency > best.dmDisplayFrequency))
            {
                best = mode;
                found = true;
            }
        }

        return found ? best : null;
    }

    private static bool IsPrimary(string gdiName)
    {
        if (string.IsNullOrWhiteSpace(gdiName))
        {
            return false;
        }

        var device = new DisplayDevice
        {
            cb = System.Runtime.InteropServices.Marshal.SizeOf<DisplayDevice>()
        };

        for (uint i = 0; DisplayDeviceInterop.EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if (string.Equals(device.DeviceName, gdiName, StringComparison.OrdinalIgnoreCase))
            {
                return (device.StateFlags & DisplayDeviceInterop.DisplayDevicePrimaryDevice) != 0;
            }

            device.cb = System.Runtime.InteropServices.Marshal.SizeOf<DisplayDevice>();
        }

        return false;
    }

    private static bool TryQuery(uint flags, out DisplayConfigPathInfo[] paths, out DisplayConfigModeInfo[] modes)
    {
        paths = [];
        modes = [];

        for (int attempt = 0; attempt < 3; attempt++)
        {
            int sizeResult = CcdInterop.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (sizeResult != CcdInterop.ErrorSuccess)
            {
                AppLog.Write($"GetDisplayConfigBufferSizes flags=0x{flags:X} result={sizeResult}");
                return false;
            }

            paths = new DisplayConfigPathInfo[pathCount];
            modes = new DisplayConfigModeInfo[modeCount];
            uint queryPaths = pathCount;
            uint queryModes = modeCount;
            int queryResult = CcdInterop.QueryDisplayConfig(flags, ref queryPaths, paths, ref queryModes, modes, IntPtr.Zero);
            if (queryResult == CcdInterop.ErrorSuccess)
            {
                if (queryPaths != pathCount)
                {
                    Array.Resize(ref paths, (int)queryPaths);
                }

                if (queryModes != modeCount)
                {
                    Array.Resize(ref modes, (int)queryModes);
                }

                return true;
            }

            AppLog.Write($"QueryDisplayConfig flags=0x{flags:X} result={queryResult}");
            if (queryResult != CcdInterop.ErrorInsufficientBuffer)
            {
                return false;
            }
        }

        return false;
    }
}
