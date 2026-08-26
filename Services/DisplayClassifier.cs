using MonitorTVSwitcher.Models;

namespace MonitorTVSwitcher.Services;

internal static class DisplayClassifier
{
    private static readonly string[] TvTokens =
    [
        "TV", "BRAVIA", "OLED", "QLED", "QNED", "SAMSUNG", "SONY", "TCL",
        "HISENSE", "VIZIO", "PHILIPS", "PANASONIC", "SHARP", "LG",
        "HDMI"
    ];

    public static DisplayRole Guess(DisplayInfo display)
    {
        if (IsBuiltInPanel(display))
        {
            return DisplayRole.Monitor;
        }

        string name = display.FriendlyName ?? "";
        foreach (string token in TvTokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return DisplayRole.Tv;
            }
        }

        return DisplayRole.Monitor;
    }

    public static DisplayRole Resolve(DisplayInfo display, AppSettings settings)
    {
        var overrideRole = settings.Displays
            .FirstOrDefault(d => string.Equals(d.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase))
            ?.Role;

        if (overrideRole is DisplayRole.Tv or DisplayRole.Monitor)
        {
            return overrideRole.Value;
        }

        return Guess(display);
    }

    private static bool IsBuiltInPanel(DisplayInfo display)
    {
        return display.OutputTechnology is Native.OutputTechnology.Internal
            or Native.OutputTechnology.Lvds
            or Native.OutputTechnology.DisplayPortEmbedded
            or Native.OutputTechnology.UdiEmbedded;
    }
}
