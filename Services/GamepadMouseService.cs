using MonitorTVSwitcher.Native;

namespace MonitorTVSwitcher.Services;

internal sealed class GamepadMouseService
{
    private const float Deadzone = 0.15f;
    private const int AxisCenter = 32767;
    private const int AxisExtent = 32767;

    private bool _enabled;
    private bool _leftDown;
    private bool _rightDown;

    public event Action<bool>? EnabledChanged;

    public int Speed { get; set; } = 50;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!value)
            {
                ReleaseButtons();
            }

            AppLog.Write($"Gamepad mouse enabled={value}");
            EnabledChanged?.Invoke(value);
        }
    }

    public void Apply(int x, int y, bool leftDown, bool rightDown)
    {
        if (!_enabled)
        {
            return;
        }

        float nx = NormalizeAxis(x);
        float ny = NormalizeAxis(y);
        float magnitude = MathF.Sqrt((nx * nx) + (ny * ny));
        SetButton(ref _leftDown, leftDown, SendInputInterop.LeftDown, SendInputInterop.LeftUp);
        SetButton(ref _rightDown, rightDown, SendInputInterop.RightDown, SendInputInterop.RightUp);

        if (magnitude > Deadzone)
        {
            float scaled = (magnitude - Deadzone) / (1f - Deadzone);
            scaled = Math.Clamp(scaled, 0f, 1f);
            scaled *= scaled;
            float speed = scaled * PixelsPerPoll();
            int dx = (int)MathF.Round(nx / magnitude * speed);
            int dy = (int)MathF.Round(ny / magnitude * speed);
            SendInputInterop.Move(dx, dy);
        }
    }

    public void ReleaseButtons()
    {
        SetButton(ref _leftDown, false, SendInputInterop.LeftDown, SendInputInterop.LeftUp);
        SetButton(ref _rightDown, false, SendInputInterop.RightDown, SendInputInterop.RightUp);
    }

    private float PixelsPerPoll()
    {
        int speed = Math.Clamp(Speed, 1, 100);
        return 4f + (speed / 100f) * 36f;
    }

    private static float NormalizeAxis(int value)
    {
        return Math.Clamp((value - AxisCenter) / (float)AxisExtent, -1f, 1f);
    }

    private static void SetButton(ref bool held, bool down, Action press, Action release)
    {
        if (down == held)
        {
            return;
        }

        held = down;
        if (down)
        {
            press();
        }
        else
        {
            release();
        }
    }
}
