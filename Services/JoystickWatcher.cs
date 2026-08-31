using MonitorTVSwitcher.Models;
using Vortice.DirectInput;

namespace MonitorTVSwitcher.Services;

internal sealed class JoystickWatcher : IDisposable
{
    public const int SequenceTimeoutMs = 800;

    private readonly IntPtr _hwnd;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _rescanTimer;
    private readonly object _gate = new();

    private IDirectInput8? _directInput;
    private readonly List<TrackedDevice> _devices = [];
    private readonly Dictionary<Guid, bool[]> _previousButtons = [];

    private bool _disposed;
    private bool _armed = true;
    private bool _mouseArmed = true;
    private int _sequenceIndex;
    private int _mouseSequenceIndex;
    private DateTime _lastSequenceAt = DateTime.MinValue;
    private DateTime _lastMouseSequenceAt = DateTime.MinValue;
    private DateTime _lastEmptyPollLog = DateTime.MinValue;

    private readonly List<int> _recordButtons = [];

    public JoystickWatcher(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _pollTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _pollTimer.Tick += (_, _) => Poll();
        _rescanTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _rescanTimer.Tick += (_, _) => Rescan();
    }

    public event Action? Triggered;
    public event Action? MouseToggleRequested;
    public event Action<IReadOnlyList<int>>? BindingCaptured;
    public event Action? DevicesChanged;

    public bool IsBusy { get; set; }
    public bool IsRecording { get; private set; }
    public TriggerKind RecordKind { get; private set; } = TriggerKind.Chord;
    public RecordTarget RecordTarget { get; private set; } = RecordTarget.DisplaySwitch;
    public IReadOnlyList<string> ConnectedNames { get; private set; } = [];
    public GamepadMouseService? Mouse { get; set; }

    public AppSettings CurrentSettings { get; set; } = new();

    public void Start()
    {
        try
        {
            _directInput = DInput.DirectInput8Create();
        }
        catch
        {
            _directInput = null;
        }

        Rescan();
        _pollTimer.Start();
        _rescanTimer.Start();
    }

    public void Reacquire()
    {
        lock (_gate)
        {
            foreach (var tracked in _devices)
            {
                try
                {
                    tracked.Device.Acquire();
                }
                catch
                {
                    // Keep the existing device; the next poll will retry Acquire.
                }
            }
        }
    }

    public void BeginRecord(TriggerKind kind, RecordTarget target)
    {
        lock (_gate)
        {
            IsRecording = true;
            RecordKind = kind;
            RecordTarget = target;
            _recordButtons.Clear();
            _sequenceIndex = 0;
            _mouseSequenceIndex = 0;
        }
    }

    public void CancelRecord()
    {
        lock (_gate)
        {
            IsRecording = false;
            _recordButtons.Clear();
        }
    }

    public void StopRecord()
    {
        List<int> captured;
        lock (_gate)
        {
            IsRecording = false;
            captured = _recordButtons.ToList();
        }

        BindingCaptured?.Invoke(captured);
    }

    public void ResetSequence()
    {
        lock (_gate)
        {
            _sequenceIndex = 0;
            _mouseSequenceIndex = 0;
            _lastSequenceAt = DateTime.MinValue;
            _lastMouseSequenceAt = DateTime.MinValue;
        }
    }

    public void Rescan()
    {
        if (_directInput is null)
        {
            return;
        }

        var instances = new Dictionary<Guid, DeviceInstance>();
        try
        {
            foreach (var device in _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                instances[device.InstanceGuid] = device;
            }

            foreach (var type in new[] { DeviceType.Gamepad, DeviceType.Joystick })
            {
                foreach (var device in _directInput.GetDevices(type, DeviceEnumerationFlags.AttachedOnly))
                {
                    instances[device.InstanceGuid] = device;
                }
            }
        }
        catch
        {
            return;
        }

        bool changed = false;
        lock (_gate)
        {
            if (instances.Count == 0 && _devices.Count > 0)
            {
                return;
            }

            var current = _devices.Select(d => d.Guid).ToHashSet();
            var next = instances.Keys.ToHashSet();
            if (!current.SetEquals(next))
            {
                changed = true;
            }

            foreach (var tracked in _devices.ToList())
            {
                if (!next.Contains(tracked.Guid))
                {
                    tracked.Dispose();
                    _devices.Remove(tracked);
                    _previousButtons.Remove(tracked.Guid);
                }
            }

            foreach (var instance in instances.Values)
            {
                if (_devices.Any(d => d.Guid == instance.InstanceGuid))
                {
                    continue;
                }

                try
                {
                    var device = _directInput.CreateDevice(instance.InstanceGuid);
                    device.SetCooperativeLevel(_hwnd, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                    device.SetDataFormat<RawJoystickState>();
                    device.Acquire();
                    _devices.Add(new TrackedDevice(
                        instance.InstanceGuid,
                        string.IsNullOrWhiteSpace(instance.InstanceName) ? instance.ProductName : instance.InstanceName,
                        device));
                    changed = true;
                }
                catch
                {
                    // Device may have disconnected while opening.
                }
            }

            ConnectedNames = _devices.Select(d => d.Name).ToList();
        }

        if (changed)
        {
            DevicesChanged?.Invoke();
        }
    }

    private void Poll()
    {
        List<DeviceSnapshot> snapshots = [];
        lock (_gate)
        {
            foreach (var tracked in _devices.ToList())
            {
                try
                {
                    tracked.Device.Poll();
                    var state = tracked.Device.GetCurrentJoystickState();
                    snapshots.Add(new DeviceSnapshot(
                        tracked.Guid,
                        (bool[])state.Buttons.Clone(),
                        state.X,
                        state.Y));
                }
                catch (Exception ex)
                {
                    if ((DateTime.Now - _lastEmptyPollLog).TotalSeconds >= 1)
                    {
                        _lastEmptyPollLog = DateTime.Now;
                        AppLog.Write($"DirectInput poll failed: {ex.Message}");
                    }

                    try
                    {
                        tracked.Device.Acquire();
                    }
                    catch (Exception acquireEx)
                    {
                        if ((DateTime.Now - _lastEmptyPollLog).TotalSeconds >= 1)
                        {
                            AppLog.Write($"DirectInput acquire failed: {acquireEx.Message}");
                        }
                    }
                }
            }
        }

        if (snapshots.Count == 0)
        {
            if (Mouse is { Enabled: true } && (DateTime.Now - _lastEmptyPollLog).TotalSeconds >= 1)
            {
                _lastEmptyPollLog = DateTime.Now;
                AppLog.Write("Gamepad poll produced no snapshots while mouse enabled; reacquiring.");
                Reacquire();
            }

            return;
        }

        if (IsRecording)
        {
            HandleRecording(snapshots);
            StorePrevious(snapshots);
            return;
        }

        if (IsBusy)
        {
            ResetSequence();
            Mouse?.ReleaseButtons();
            StorePrevious(snapshots);
            return;
        }

        var settings = CurrentSettings;
        bool mouseEnabled = Mouse is { Enabled: true };

        var toggleSnapshots = mouseEnabled
            ? WithoutClickButtons(snapshots)
            : snapshots;

        bool mouseToggled = TryTrigger(
            settings.MouseButtons,
            settings.MouseTriggerKind,
            toggleSnapshots,
            ref _mouseArmed,
            ref _mouseSequenceIndex,
            ref _lastMouseSequenceAt,
            MouseToggleRequested);

        if (mouseEnabled)
        {
            ApplyMouse(snapshots);
        }

        if (mouseToggled)
        {
            _sequenceIndex = 0;
            _lastSequenceAt = DateTime.MinValue;
            StorePrevious(snapshots);
            return;
        }

        var displaySnapshots = mouseEnabled
            ? WithoutClickButtons(snapshots)
            : snapshots;

        TryTrigger(
            settings.Buttons,
            settings.TriggerKind,
            displaySnapshots,
            ref _armed,
            ref _sequenceIndex,
            ref _lastSequenceAt,
            Triggered);

        StorePrevious(snapshots);
    }

    private void ApplyMouse(List<DeviceSnapshot> snapshots)
    {
        if (Mouse is null)
        {
            return;
        }

        int bestX = 32767;
        int bestY = 32767;
        float bestMagnitude = -1f;
        int leftButton = CurrentSettings.MouseLeftButton;
        int rightButton = CurrentSettings.MouseRightButton;
        bool leftDown = false;
        bool rightDown = false;

        foreach (var snapshot in snapshots)
        {
            float nx = NormalizeAxis(snapshot.X);
            float ny = NormalizeAxis(snapshot.Y);
            float magnitude = MathF.Sqrt((nx * nx) + (ny * ny));
            if (magnitude > bestMagnitude)
            {
                bestMagnitude = magnitude;
                bestX = snapshot.X;
                bestY = snapshot.Y;
            }

            if (IsButtonDown(snapshot.Buttons, leftButton))
            {
                leftDown = true;
            }

            if (IsButtonDown(snapshot.Buttons, rightButton))
            {
                rightDown = true;
            }
        }

        Mouse.Speed = CurrentSettings.MouseSpeed;
        Mouse.Apply(bestX, bestY, leftDown, rightDown);
    }

    private static float NormalizeAxis(int value)
    {
        return Math.Clamp((value - 32767) / 32767f, -1f, 1f);
    }

    private void HandleRecording(List<DeviceSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            _previousButtons.TryGetValue(snapshot.Guid, out var previous);
            for (int i = 0; i < snapshot.Buttons.Length; i++)
            {
                bool wasDown = previous is not null && i < previous.Length && previous[i];
                bool isDown = snapshot.Buttons[i];
                if (isDown && !wasDown)
                {
                    if (RecordTarget is RecordTarget.LeftClick or RecordTarget.RightClick)
                    {
                        _recordButtons.Clear();
                        _recordButtons.Add(i);
                        StopRecord();
                        return;
                    }

                    if (RecordKind == TriggerKind.Chord)
                    {
                        if (!_recordButtons.Contains(i))
                        {
                            _recordButtons.Add(i);
                        }
                    }
                    else
                    {
                        _recordButtons.Add(i);
                        _lastSequenceAt = DateTime.UtcNow;
                    }
                }
            }
        }

        if (RecordKind == TriggerKind.Chord && _recordButtons.Count > 0 && !AnyBoundDown(_recordButtons, snapshots))
        {
            StopRecord();
            return;
        }

        if (RecordKind == TriggerKind.Sequence
            && _recordButtons.Count > 0
            && (DateTime.UtcNow - _lastSequenceAt).TotalMilliseconds >= SequenceTimeoutMs)
        {
            StopRecord();
        }
    }

    private bool TryTrigger(
        IReadOnlyList<int> buttons,
        TriggerKind kind,
        List<DeviceSnapshot> snapshots,
        ref bool armed,
        ref int sequenceIndex,
        ref DateTime lastSequenceAt,
        Action? handler)
    {
        if (buttons.Count == 0)
        {
            return false;
        }

        if (kind == TriggerKind.Chord)
        {
            return UpdateChord(buttons, snapshots, ref armed, handler);
        }

        return UpdateSequence(buttons, snapshots, ref sequenceIndex, ref lastSequenceAt, handler);
    }

    private static bool UpdateChord(
        IReadOnlyList<int> buttons,
        List<DeviceSnapshot> snapshots,
        ref bool armed,
        Action? handler)
    {
        bool down = snapshots.Any(s => buttons.All(b => b >= 0 && b < s.Buttons.Length && s.Buttons[b]));
        if (down)
        {
            if (armed)
            {
                armed = false;
                handler?.Invoke();
                return true;
            }

            return false;
        }

        armed = true;
        return false;
    }

    private bool UpdateSequence(
        IReadOnlyList<int> buttons,
        List<DeviceSnapshot> snapshots,
        ref int sequenceIndex,
        ref DateTime lastSequenceAt,
        Action? handler)
    {
        if (sequenceIndex > 0 && (DateTime.UtcNow - lastSequenceAt).TotalMilliseconds >= SequenceTimeoutMs)
        {
            sequenceIndex = 0;
        }

        var presses = new List<int>();
        foreach (var snapshot in snapshots)
        {
            _previousButtons.TryGetValue(snapshot.Guid, out var previous);
            for (int i = 0; i < snapshot.Buttons.Length; i++)
            {
                bool wasDown = previous is not null && i < previous.Length && previous[i];
                if (snapshot.Buttons[i] && !wasDown)
                {
                    presses.Add(i);
                }
            }
        }

        foreach (int pressed in presses)
        {
            if (pressed == buttons[sequenceIndex])
            {
                sequenceIndex++;
                lastSequenceAt = DateTime.UtcNow;
                if (sequenceIndex >= buttons.Count)
                {
                    sequenceIndex = 0;
                    handler?.Invoke();
                    return true;
                }
            }
            else
            {
                sequenceIndex = pressed == buttons[0] ? 1 : 0;
                if (sequenceIndex == 1)
                {
                    lastSequenceAt = DateTime.UtcNow;
                }
            }
        }

        return false;
    }

    private static bool AnyBoundDown(IReadOnlyList<int> buttons, List<DeviceSnapshot> snapshots)
    {
        return snapshots.Any(s => buttons.Any(b => b >= 0 && b < s.Buttons.Length && s.Buttons[b]));
    }

    private static bool IsButtonDown(bool[] buttons, int index)
    {
        return index >= 0 && index < buttons.Length && buttons[index];
    }

    private List<DeviceSnapshot> WithoutClickButtons(List<DeviceSnapshot> snapshots)
    {
        int left = CurrentSettings.MouseLeftButton;
        int right = CurrentSettings.MouseRightButton;
        var filtered = new List<DeviceSnapshot>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var buttons = (bool[])snapshot.Buttons.Clone();
            if (left >= 0 && left < buttons.Length)
            {
                buttons[left] = false;
            }

            if (right >= 0 && right < buttons.Length)
            {
                buttons[right] = false;
            }

            filtered.Add(snapshot with { Buttons = buttons });
        }

        return filtered;
    }

    private void StorePrevious(List<DeviceSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            _previousButtons[snapshot.Guid] = snapshot.Buttons;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _rescanTimer.Stop();
        _rescanTimer.Dispose();
        Mouse?.ReleaseButtons();

        lock (_gate)
        {
            foreach (var device in _devices)
            {
                device.Dispose();
            }

            _devices.Clear();
            _directInput?.Dispose();
            _directInput = null;
        }
    }

    private readonly record struct DeviceSnapshot(Guid Guid, bool[] Buttons, int X, int Y);

    private sealed class TrackedDevice(Guid guid, string name, IDirectInputDevice8 device) : IDisposable
    {
        public Guid Guid { get; } = guid;
        public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "Joystick" : name;
        public IDirectInputDevice8 Device { get; } = device;

        public void Dispose()
        {
            try
            {
                Device.Unacquire();
            }
            catch
            {
                // Already disconnected.
            }

            Device.Dispose();
        }
    }
}
