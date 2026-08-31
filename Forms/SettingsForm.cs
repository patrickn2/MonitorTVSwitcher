using MonitorTVSwitcher.Models;
using MonitorTVSwitcher.Services;

namespace MonitorTVSwitcher.Forms;

internal sealed class SettingsForm : Form
{
    private readonly JoystickWatcher _joystick;
    private readonly DisplaySwitchService _switcher;
    private readonly GamepadMouseService _mouse;
    private AppSettings _settings;

    private readonly RadioButton _chordRadio = new() { Text = "Combination (hold together)", AutoSize = true };
    private readonly RadioButton _sequenceRadio = new() { Text = "Sequence (press in order)", AutoSize = true };
    private readonly Label _bindingLabel = new() { AutoSize = true, MaximumSize = new Size(480, 0) };
    private readonly Button _recordButton = new() { Text = "Record", AutoSize = true };
    private readonly RadioButton _mouseChordRadio = new() { Text = "Combination (hold together)", AutoSize = true };
    private readonly RadioButton _mouseSequenceRadio = new() { Text = "Sequence (press in order)", AutoSize = true };
    private readonly Label _mouseBindingLabel = new() { AutoSize = true, MaximumSize = new Size(480, 0) };
    private readonly Button _mouseRecordButton = new() { Text = "Record", AutoSize = true };
    private readonly Label _leftClickLabel = new() { AutoSize = true };
    private readonly Button _leftClickRecordButton = new() { Text = "Record", AutoSize = true };
    private readonly Label _rightClickLabel = new() { AutoSize = true };
    private readonly Button _rightClickRecordButton = new() { Text = "Record", AutoSize = true };
    private readonly Label _speedLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TrackBar _speedBar = new()
    {
        Minimum = 1,
        Maximum = 100,
        TickFrequency = 10,
        Width = 220,
        AutoSize = true
    };
    private readonly Button _mouseTestButton = new() { Text = "Test mouse", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly TableLayoutPanel _displayTable = new()
    {
        ColumnCount = 4,
        AutoSize = true,
        Dock = DockStyle.Top
    };
    private readonly Label _statusLabel = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    private bool _suppressEvents;

    private const int WmClose = 0x0010;
    private const int WmSysCommand = 0x0112;
    private const int WmNcLButtonDown = 0x00A1;
    private const int ScClose = 0xF060;
    private const int HtClose = 20;
    private const int HtCaption = 2;

    private bool _movingForm;
    private Point _moveCursorOrigin;
    private Point _moveFormOrigin;
    private readonly System.Windows.Forms.Timer _formMoveTimer = new() { Interval = 16 };

    public SettingsForm(JoystickWatcher joystick, DisplaySwitchService switcher, GamepadMouseService mouse, AppSettings settings)
    {
        _joystick = joystick;
        _switcher = switcher;
        _mouse = mouse;
        _settings = settings;

        Text = AppInfo.DisplayName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 760);
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        root.Controls.Add(BuildTriggerGroup(), 0, 0);
        root.Controls.Add(BuildMouseGroup(), 0, 1);
        root.Controls.Add(_startWithWindows, 0, 2);
        root.Controls.Add(BuildDisplayGroup(), 0, 3);
        root.Controls.Add(_statusLabel, 0, 4);

        var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => ScheduleHide();
        var closeHost = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        closeHost.Controls.Add(close);
        root.Controls.Add(closeHost, 0, 5);

        Controls.Add(root);

        _chordRadio.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents || !_chordRadio.Checked)
            {
                return;
            }

            _settings.TriggerKind = TriggerKind.Chord;
            Save();
        };
        _sequenceRadio.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents || !_sequenceRadio.Checked)
            {
                return;
            }

            _settings.TriggerKind = TriggerKind.Sequence;
            Save();
        };
        _recordButton.Click += (_, _) => ToggleRecord(RecordTarget.DisplaySwitch);
        _mouseChordRadio.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents || !_mouseChordRadio.Checked)
            {
                return;
            }

            _settings.MouseTriggerKind = TriggerKind.Chord;
            Save();
        };
        _mouseSequenceRadio.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents || !_mouseSequenceRadio.Checked)
            {
                return;
            }

            _settings.MouseTriggerKind = TriggerKind.Sequence;
            Save();
        };
        _mouseRecordButton.Click += (_, _) => ToggleRecord(RecordTarget.Mouse);
        _leftClickRecordButton.Click += (_, _) => ToggleRecord(RecordTarget.LeftClick);
        _rightClickRecordButton.Click += (_, _) => ToggleRecord(RecordTarget.RightClick);
        _speedBar.ValueChanged += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            _settings.MouseSpeed = _speedBar.Value;
            _mouse.Speed = _settings.MouseSpeed;
            UpdateSpeedLabel();
            Save();
        };
        _mouseTestButton.Click += (_, _) => ToggleMouseTest();
        _startWithWindows.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            try
            {
                StartupService.SetEnabled(_startWithWindows.Checked);
                _settings.StartWithWindows = _startWithWindows.Checked;
                Save();
            }
            catch (Exception ex)
            {
                _suppressEvents = true;
                _startWithWindows.Checked = StartupService.IsEnabled();
                _suppressEvents = false;
                MessageBox.Show(this, ex.Message, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        _joystick.BindingCaptured += OnBindingCaptured;
        _joystick.DevicesChanged += UpdateStatus;
        _switcher.StatusChanged += OnSwitcherStatus;
        _switcher.BusyChanged += OnSwitcherBusy;
        _mouse.EnabledChanged += OnMouseEnabledChanged;
        FormClosing += OnSettingsFormClosing;
        FormClosed += (_, _) =>
        {
            _formMoveTimer.Stop();
            _formMoveTimer.Dispose();
            _joystick.BindingCaptured -= OnBindingCaptured;
            _joystick.DevicesChanged -= UpdateStatus;
            _switcher.StatusChanged -= OnSwitcherStatus;
            _switcher.BusyChanged -= OnSwitcherBusy;
            _mouse.EnabledChanged -= OnMouseEnabledChanged;
            if (_joystick.IsRecording)
            {
                _joystick.CancelRecord();
            }
        };
        _formMoveTimer.Tick += (_, _) => UpdateFormMove();

        LoadValues();
        RebuildDisplayRows();
        UpdateStatus();
    }

    protected override void WndProc(ref Message m)
    {
        // Title-bar clicks run a nested DefWindowProc loop that waits for mouse-up.
        // Gamepad LMB is injected with SendInput on this same UI thread, so that
        // loop never sees mouse-up until a real mouse click. Hide on NC-down instead.
        if (m.Msg == WmNcLButtonDown)
        {
            int hit = (int)m.WParam;
            if (hit == HtClose)
            {
                AppLog.Write("Settings title-bar X click; hiding without title-bar mouse track.");
                HideSettings();
                return;
            }

            if (hit == HtCaption && _mouse.Enabled)
            {
                _movingForm = true;
                _moveCursorOrigin = Cursor.Position;
                _moveFormOrigin = Location;
                _formMoveTimer.Start();
                AppLog.Write("Settings title-bar drag started.");
                return;
            }

            if (_mouse.Enabled)
            {
                AppLog.Write($"Settings title-bar click ignored hit={hit} (gamepad mouse on)");
                return;
            }
        }

        if (m.Msg == WmClose
            || (m.Msg == WmSysCommand && ((int)m.WParam & 0xFFF0) == ScClose))
        {
            AppLog.Write($"Settings close intercepted msg=0x{m.Msg:X}");
            HideSettings();
            return;
        }

        base.WndProc(ref m);
    }

    private void OnSettingsFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.WindowsShutDown
            or CloseReason.TaskManagerClosing
            or CloseReason.ApplicationExitCall)
        {
            return;
        }

        e.Cancel = true;
        AppLog.Write($"Settings closing canceled reason={e.CloseReason}");
        HideSettings();
    }

    private void ScheduleHide()
    {
        HideSettings();
    }

    private void UpdateFormMove()
    {
        if (!_movingForm)
        {
            _formMoveTimer.Stop();
            return;
        }

        if (!_mouse.Enabled || (Control.MouseButtons & MouseButtons.Left) == 0)
        {
            _movingForm = false;
            _formMoveTimer.Stop();
            return;
        }

        Point now = Cursor.Position;
        Location = new Point(
            _moveFormOrigin.X + now.X - _moveCursorOrigin.X,
            _moveFormOrigin.Y + now.Y - _moveCursorOrigin.Y);
    }

    private void HideSettings()
    {
        if (IsDisposed)
        {
            return;
        }

        if (_joystick.IsRecording)
        {
            _joystick.CancelRecord();
            ResetRecordButtons();
            UpdateBindingLabel();
            UpdateMouseBindingLabel();
            UpdateClickLabels();
        }

        _movingForm = false;
        _formMoveTimer.Stop();
        // The X click is a gamepad LMB via SendInput. If poll drops for a frame,
        // LeftUp never goes out and the pointer looks dead. Release first.
        _mouse.ReleaseButtons();
        Hide();
        _joystick.Reacquire();
        AppLog.Write($"Settings hidden; gamepad mouse enabled={_mouse.Enabled}");
    }

    private Control BuildTriggerGroup()
    {
        var group = new GroupBox
        {
            Text = "Joystick trigger",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8)
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false
        };
        layout.Controls.Add(_chordRadio);
        layout.Controls.Add(_sequenceRadio);
        layout.Controls.Add(_bindingLabel);
        layout.Controls.Add(_recordButton);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildMouseGroup()
    {
        var group = new GroupBox
        {
            Text = "Gamepad mouse control",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8)
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false
        };
        layout.Controls.Add(new Label
        {
            Text = "Left stick moves the pointer on any screen. Record which buttons click, then use Test mouse to try it.",
            AutoSize = true,
            MaximumSize = new Size(500, 0)
        });
        layout.Controls.Add(_mouseChordRadio);
        layout.Controls.Add(_mouseSequenceRadio);
        layout.Controls.Add(_mouseBindingLabel);
        layout.Controls.Add(_mouseRecordButton);
        layout.Controls.Add(BuildClickRow("Left click (LMB)", _leftClickLabel, _leftClickRecordButton));
        layout.Controls.Add(BuildClickRow("Right click (RMB)", _rightClickLabel, _rightClickRecordButton));
        layout.Controls.Add(_speedLabel);
        layout.Controls.Add(_speedBar);
        layout.Controls.Add(_mouseTestButton);
        group.Controls.Add(layout);
        return group;
    }

    private static Control BuildClickRow(string caption, Label value, Button record)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 0)
        };
        row.Controls.Add(new Label
        {
            Text = caption + ":",
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0)
        });
        row.Controls.Add(value);
        row.Controls.Add(record);
        return row;
    }

    private Control BuildDisplayGroup()
    {
        var group = new GroupBox
        {
            Text = "Displays",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _displayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        _displayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _displayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        _displayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        scroll.Controls.Add(_displayTable);
        group.Controls.Add(scroll);
        return group;
    }

    private void LoadValues()
    {
        _suppressEvents = true;
        _chordRadio.Checked = _settings.TriggerKind == TriggerKind.Chord;
        _sequenceRadio.Checked = _settings.TriggerKind == TriggerKind.Sequence;
        _mouseChordRadio.Checked = _settings.MouseTriggerKind == TriggerKind.Chord;
        _mouseSequenceRadio.Checked = _settings.MouseTriggerKind == TriggerKind.Sequence;
        _speedBar.Value = Math.Clamp(_settings.MouseSpeed, 1, 100);
        _startWithWindows.Checked = StartupService.IsEnabled();
        _settings.StartWithWindows = _startWithWindows.Checked;
        _suppressEvents = false;
        UpdateBindingLabel();
        UpdateMouseBindingLabel();
        UpdateClickLabels();
        UpdateSpeedLabel();
        UpdateMouseTestButton();
    }

    private void RebuildDisplayRows()
    {
        _displayTable.SuspendLayout();
        _displayTable.Controls.Clear();
        _displayTable.RowStyles.Clear();
        _displayTable.RowCount = 0;

        AddHeader("Display", 0);
        AddHeader("Role", 1);
        AddHeader("Sound", 2);
        AddHeader("", 3);

        var displays = DisplayService.Enumerate();
        var audio = AudioService.GetPlaybackDevices();

        int row = 1;
        foreach (var display in displays)
        {
            _displayTable.RowCount++;
            var mapping = _settings.GetOrCreateOverride(display.DevicePath);

            var name = new Label
            {
                Text = display.MenuLabel,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 8, 3, 3)
            };

            var role = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 110,
                Tag = display.DevicePath
            };
            role.Items.AddRange(["Auto", "TV", "Monitor"]);
            role.SelectedIndex = mapping.Role switch
            {
                DisplayRole.Tv => 1,
                DisplayRole.Monitor => 2,
                _ => 0
            };
            role.SelectedIndexChanged += RoleChanged;

            var sound = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170,
                Tag = display.DevicePath,
                DisplayMember = "Name",
                ValueMember = "Id"
            };
            var autoItem = new AudioChoice("Auto", null);
            sound.Items.Add(autoItem);
            foreach (var device in audio)
            {
                sound.Items.Add(new AudioChoice(device.Name, device.Id));
            }

            int selected = 0;
            if (!string.IsNullOrWhiteSpace(mapping.PlaybackDeviceId))
            {
                for (int i = 1; i < sound.Items.Count; i++)
                {
                    if (sound.Items[i] is AudioChoice choice
                        && string.Equals(choice.Id, mapping.PlaybackDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = i;
                        break;
                    }
                }
            }

            sound.SelectedIndex = selected;
            sound.SelectedIndexChanged += SoundChanged;

            var switchButton = new Button
            {
                Text = "Switch",
                AutoSize = true,
                Tag = display.DevicePath,
                Enabled = !_switcher.IsBusy
            };
            switchButton.Click += (_, _) =>
            {
                AppLog.Write($"Settings Switch clicked {display.MenuLabel}");
                _switcher.SwitchTo((string)switchButton.Tag!);
            };

            _displayTable.Controls.Add(name, 0, row);
            _displayTable.Controls.Add(role, 1, row);
            _displayTable.Controls.Add(sound, 2, row);
            _displayTable.Controls.Add(switchButton, 3, row);
            row++;
        }

        if (displays.Count == 0)
        {
            _displayTable.RowCount++;
            _displayTable.Controls.Add(new Label
            {
                Text = "No displays were detected.",
                AutoSize = true
            }, 0, row);
        }

        _displayTable.ResumeLayout();
        Save();
    }

    private void AddHeader(string text, int column)
    {
        if (_displayTable.RowCount == 0)
        {
            _displayTable.RowCount = 1;
        }

        _displayTable.Controls.Add(new Label
        {
            Text = text,
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true
        }, column, 0);
    }

    private void RoleChanged(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not string path)
        {
            return;
        }

        var mapping = _settings.GetOrCreateOverride(path);
        mapping.Role = combo.SelectedIndex switch
        {
            1 => DisplayRole.Tv,
            2 => DisplayRole.Monitor,
            _ => DisplayRole.Auto
        };
        Save();
    }

    private void SoundChanged(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not string path)
        {
            return;
        }

        var mapping = _settings.GetOrCreateOverride(path);
        mapping.PlaybackDeviceId = combo.SelectedItem is AudioChoice choice ? choice.Id : null;
        Save();
    }

    private void ToggleRecord(RecordTarget target)
    {
        if (_joystick.IsRecording && _joystick.RecordTarget == target)
        {
            _joystick.StopRecord();
            return;
        }

        if (_joystick.IsRecording)
        {
            _joystick.CancelRecord();
            ResetRecordButtons();
            UpdateBindingLabel();
            UpdateMouseBindingLabel();
            UpdateClickLabels();
        }

        TriggerKind kind;
        Button recordButton;
        Label bindingLabel;
        string prompt;
        if (target == RecordTarget.Mouse)
        {
            kind = _settings.MouseTriggerKind;
            recordButton = _mouseRecordButton;
            bindingLabel = _mouseBindingLabel;
            prompt = kind == TriggerKind.Chord
                ? "Hold the buttons, then release…"
                : "Press the buttons in order…";
        }
        else if (target == RecordTarget.LeftClick)
        {
            kind = TriggerKind.Sequence;
            recordButton = _leftClickRecordButton;
            bindingLabel = _leftClickLabel;
            prompt = "Press a button…";
        }
        else if (target == RecordTarget.RightClick)
        {
            kind = TriggerKind.Sequence;
            recordButton = _rightClickRecordButton;
            bindingLabel = _rightClickLabel;
            prompt = "Press a button…";
        }
        else
        {
            kind = _settings.TriggerKind;
            recordButton = _recordButton;
            bindingLabel = _bindingLabel;
            prompt = kind == TriggerKind.Chord
                ? "Hold the buttons, then release…"
                : "Press the buttons in order…";
        }

        recordButton.Text = "Stop";
        bindingLabel.Text = prompt;
        _joystick.BeginRecord(kind, target);
    }

    private void OnBindingCaptured(IReadOnlyList<int> buttons)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            switch (_joystick.RecordTarget)
            {
                case RecordTarget.Mouse:
                    _settings.MouseButtons = buttons.ToList();
                    break;
                case RecordTarget.LeftClick:
                    if (buttons.Count > 0)
                    {
                        _settings.MouseLeftButton = buttons[0];
                    }
                    break;
                case RecordTarget.RightClick:
                    if (buttons.Count > 0)
                    {
                        _settings.MouseRightButton = buttons[0];
                    }
                    break;
                default:
                    _settings.Buttons = buttons.ToList();
                    break;
            }

            ResetRecordButtons();
            UpdateBindingLabel();
            UpdateMouseBindingLabel();
            UpdateClickLabels();
            Save();
        });
    }

    private void OnSwitcherStatus(string _) => UpdateStatus();

    private void OnSwitcherBusy()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(OnSwitcherBusy);
            return;
        }

        UpdateSwitchButtons();
        UpdateStatus();
    }

    private void UpdateBindingLabel()
    {
        _bindingLabel.Text = FormatBinding(_settings.TriggerKind, _settings.Buttons);
    }

    private void UpdateMouseBindingLabel()
    {
        _mouseBindingLabel.Text = FormatBinding(_settings.MouseTriggerKind, _settings.MouseButtons);
    }

    private void UpdateClickLabels()
    {
        _leftClickLabel.Text = FormatButton(_settings.MouseLeftButton);
        _rightClickLabel.Text = FormatButton(_settings.MouseRightButton);
    }

    private void UpdateSpeedLabel()
    {
        _speedLabel.Text = $"Pointer speed: {_speedBar.Value}";
    }

    private void UpdateMouseTestButton()
    {
        _mouseTestButton.Text = _mouse.Enabled ? "Stop mouse" : "Test mouse";
    }

    private void OnMouseEnabledChanged(bool _)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnMouseEnabledChanged(_));
            return;
        }

        UpdateMouseTestButton();
        UpdateSwitchButtons();
    }

    private void UpdateSwitchButtons()
    {
        foreach (Control control in _displayTable.Controls)
        {
            if (control is Button switchButton && switchButton.Tag is string)
            {
                switchButton.Enabled = !_switcher.IsBusy;
            }
        }
    }

    private void ToggleMouseTest()
    {
        _mouse.Speed = _settings.MouseSpeed;
        _mouse.Enabled = !_mouse.Enabled;
        UpdateMouseTestButton();
    }

    private void ResetRecordButtons()
    {
        _recordButton.Text = "Record";
        _mouseRecordButton.Text = "Record";
        _leftClickRecordButton.Text = "Record";
        _rightClickRecordButton.Text = "Record";
    }

    private void UpdateStatus()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(UpdateStatus);
            return;
        }

        string joysticks = _joystick.ConnectedNames.Count == 0
            ? "No joystick connected."
            : $"{_joystick.ConnectedNames.Count} joystick(s): {string.Join(", ", _joystick.ConnectedNames)}";
        _statusLabel.Text = $"{joysticks}  {_switcher.LastStatus}";
    }

    private void Save()
    {
        _settings.MouseSpeed = Math.Clamp(_settings.MouseSpeed, 1, 100);
        _mouse.Speed = _settings.MouseSpeed;
        SettingsStore.Save(_settings);
        _joystick.CurrentSettings = _settings;
    }

    private static string FormatButton(int index)
    {
        return index < 0 ? "(not set)" : $"Button {index + 1}";
    }

    public static string FormatBinding(TriggerKind kind, IReadOnlyList<int> buttons)
    {
        if (buttons.Count == 0)
        {
            return "Buttons: (not set)";
        }

        IEnumerable<string> names = buttons.Select(i => $"Button {i + 1}");
        string joined = kind == TriggerKind.Chord
            ? string.Join(" + ", names)
            : string.Join(" → ", names);
        return "Buttons: " + joined;
    }

    private sealed record AudioChoice(string Name, string? Id)
    {
        public override string ToString() => Name;
    }
}
