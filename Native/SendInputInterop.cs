using System.Runtime.InteropServices;

namespace MonitorTVSwitcher.Native;

internal static class SendInputInterop
{
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        Send(MouseMove, dx, dy);
    }

    public static void LeftDown() => Send(MouseLeftDown);
    public static void LeftUp() => Send(MouseLeftUp);
    public static void RightDown() => Send(MouseRightDown);
    public static void RightUp() => Send(MouseRightUp);

    private static void Send(uint flags, int dx = 0, int dy = 0)
    {
        var inputs = new INPUT[1];
        inputs[0].type = InputMouse;
        inputs[0].union.mouse.dx = dx;
        inputs[0].union.mouse.dy = dy;
        inputs[0].union.mouse.dwFlags = flags;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }
}
