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
    private int _sequenceIndex;
    private DateTime _lastSequenceAt = DateTime.MinValue;

    public JoystickWatcher(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _pollTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _pollTimer.Tick += (_, _) => Poll();
        _rescanTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _rescanTimer.Tick += (_, _) => Rescan();
    }

    public event Action? Triggered;
    public event Action<IReadOnlyList<int>>? BindingCaptured;
    public event Action? DevicesChanged;

    public bool IsBusy { get; set; }
    public bool IsRecording { get; private set; }
    public TriggerKind RecordKind { get; private set; } = TriggerKind.Chord;
    public IReadOnlyList<string> ConnectedNames { get; private set; } = [];

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

    public void BeginRecord(TriggerKind kind)
    {
        lock (_gate)
        {
            IsRecording = true;
            RecordKind = kind;
            _recordButtons.Clear();
            _sequenceIndex = 0;
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
            _lastSequenceAt = DateTime.MinValue;
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

    private readonly List<int> _recordButtons = [];

    private void Poll()
    {
        List<(Guid Guid, bool[] Buttons)> snapshots = [];
        lock (_gate)
        {
            foreach (var tracked in _devices.ToList())
            {
                try
                {
                    tracked.Device.Poll();
                    var state = tracked.Device.GetCurrentJoystickState();
                    snapshots.Add((tracked.Guid, (bool[])state.Buttons.Clone()));
                }
                catch
                {
                    try
                    {
                        tracked.Device.Acquire();
                    }
                    catch
                    {
                        tracked.Dispose();
                        _devices.Remove(tracked);
                        _previousButtons.Remove(tracked.Guid);
                        ConnectedNames = _devices.Select(d => d.Name).ToList();
                        DevicesChanged?.Invoke();
                    }
                }
            }
        }

        if (snapshots.Count == 0)
        {
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
            StorePrevious(snapshots);
            return;
        }

        var settings = CurrentSettings;
        if (settings.Buttons.Count == 0)
        {
            StorePrevious(snapshots);
            return;
        }

        if (settings.TriggerKind == TriggerKind.Chord)
        {
            HandleChord(settings.Buttons, snapshots);
        }
        else
        {
            HandleSequence(settings.Buttons, snapshots);
        }

        StorePrevious(snapshots);
    }

    private void HandleRecording(List<(Guid Guid, bool[] Buttons)> snapshots)
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

    private void HandleChord(IReadOnlyList<int> buttons, List<(Guid Guid, bool[] Buttons)> snapshots)
    {
        bool down = snapshots.Any(s => buttons.All(b => b >= 0 && b < s.Buttons.Length && s.Buttons[b]));
        if (down)
        {
            if (_armed)
            {
                _armed = false;
                Triggered?.Invoke();
            }
        }
        else
        {
            _armed = true;
        }
    }

    private void HandleSequence(IReadOnlyList<int> buttons, List<(Guid Guid, bool[] Buttons)> snapshots)
    {
        if (_sequenceIndex > 0 && (DateTime.UtcNow - _lastSequenceAt).TotalMilliseconds >= SequenceTimeoutMs)
        {
            _sequenceIndex = 0;
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
            if (pressed == buttons[_sequenceIndex])
            {
                _sequenceIndex++;
                _lastSequenceAt = DateTime.UtcNow;
                if (_sequenceIndex >= buttons.Count)
                {
                    _sequenceIndex = 0;
                    Triggered?.Invoke();
                    return;
                }
            }
            else
            {
                _sequenceIndex = pressed == buttons[0] ? 1 : 0;
                if (_sequenceIndex == 1)
                {
                    _lastSequenceAt = DateTime.UtcNow;
                }
            }
        }
    }

    private static bool AnyBoundDown(IReadOnlyList<int> buttons, List<(Guid Guid, bool[] Buttons)> snapshots)
    {
        return snapshots.Any(s => buttons.Any(b => b >= 0 && b < s.Buttons.Length && s.Buttons[b]));
    }

    private void StorePrevious(List<(Guid Guid, bool[] Buttons)> snapshots)
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
