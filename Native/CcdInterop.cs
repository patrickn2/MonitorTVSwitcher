using System.Runtime.InteropServices;

namespace GameShortcut.Native;

internal static class CcdInterop
{
    public const uint QdcAllPaths = 0x00000001;
    public const uint QdcOnlyActivePaths = 0x00000002;
    public const uint QdcVirtualModeAware = 0x00000010;

    public const uint SdcTopologySupplied = 0x00000010;
    public const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    public const uint SdcApply = 0x00000080;
    public const uint SdcSaveToDatabase = 0x00000200;
    public const uint SdcAllowChanges = 0x00000400;
    public const uint SdcAllowPathOrderChanges = 0x00002000;
    public const uint SdcVirtualModeAware = 0x00008000;

    public const uint PathActive = 0x00000001;
    public const uint PathModeIdxInvalid = 0xFFFFFFFF;
    public const uint TargetInUse = 0x00000001;

    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;

    public const int DeviceInfoGetSourceName = 1;
    public const int DeviceInfoGetTargetName = 2;
    public const int DeviceInfoGetTargetPreferredMode = 3;
    public const int DeviceInfoGetAdvancedColorInfo = 9;
    public const int DeviceInfoSetAdvancedColorState = 10;
    public const int DeviceInfoGetAdvancedColorInfo2 = 15;
    public const int DeviceInfoSetHdrState = 16;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[]? pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[]? modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(IntPtr setPacket);

    public static int GetDeviceInfo<T>(ref T packet) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(packet, ptr, false);
            int result = DisplayConfigGetDeviceInfo(ptr);
            packet = Marshal.PtrToStructure<T>(ptr)!;
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static int SetDeviceInfo<T>(T packet) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(packet, ptr, false);
            return DisplayConfigSetDeviceInfo(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid : IEquatable<Luid>
{
    public uint LowPart;
    public int HighPart;

    public bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;
    public override bool Equals(object? obj) => obj is Luid other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(LowPart, HighPart);
    public static bool operator ==(Luid left, Luid right) => left.Equals(right);
    public static bool operator !=(Luid left, Luid right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public OutputTechnology OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public DisplayConfigRational RefreshRate;
    public uint ScanLineOrdering;
    public int TargetAvailable;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;
}

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct DisplayConfigModeInfo
{
    [FieldOffset(0)] public uint InfoType;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public Luid AdapterId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public int Type;
    public uint Size;
    public Luid AdapterId;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Flags;
    public OutputTechnology OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string MonitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string MonitorDevicePath;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string ViewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    public ulong PixelRate;
    public DisplayConfigRational HSyncFreq;
    public DisplayConfigRational VSyncFreq;
    public DisplayConfig2DRegion ActiveSize;
    public DisplayConfig2DRegion TotalSize;
    public uint VideoStandard;
    public uint ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetPreferredMode
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Width;
    public uint Height;
    public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigGetAdvancedColorInfo
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Value;
    public uint ColorEncoding;
    public uint BitsPerColorChannel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSetAdvancedColorState
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint EnableAdvancedColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSetHdrState
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint EnableHdr;
}

internal enum OutputTechnology : uint
{
    Other = unchecked((uint)-1),
    Hd15 = 0,
    Svideo = 1,
    CompositeVideo = 2,
    ComponentVideo = 3,
    Dvi = 4,
    Hdmi = 5,
    Lvds = 6,
    DJpn = 8,
    Sdi = 9,
    DisplayPortExternal = 10,
    DisplayPortEmbedded = 11,
    UdiExternal = 12,
    UdiEmbedded = 13,
    SdtvDongle = 14,
    Miracast = 15,
    IndirectWired = 16,
    IndirectVirtual = 17,
    DisplayPortUsb = 18,
    Internal = 0x80000000
}
