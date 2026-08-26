using GameShortcut.Models;
using GameShortcut.Services;

namespace GameShortcut.Forms;

internal sealed class SettingsForm : Form
{
    private readonly JoystickWatcher _joystick;
    private readonly DisplaySwitchService _switcher;
    private AppSettings _settings;

    private readonly RadioButton _chordRadio = new() { Text = "Combination (hold together)", AutoSize = true };
    private readonly RadioButton _sequenceRadio = new() { Text = "Sequence (press in order)", AutoSize = true };
    private readonly Label _bindingLabel = new() { AutoSize = true, MaximumSize = new Size(480, 0) };
    private readonly Button _recordButton = new() { Text = "Record", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly TableLayoutPanel _displayTable = new()
    {
        ColumnCount = 4,
        AutoSize = true,
        Dock = DockStyle.Top
    };
    private readonly Label _statusLabel = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    private bool _suppressEvents;

    public SettingsForm(JoystickWatcher joystick, DisplaySwitchService switcher, AppSettings settings)
    {
        _joystick = joystick;
        _switcher = switcher;
        _settings = settings;

        Text = AppInfo.DisplayName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 520);
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        root.Controls.Add(BuildTriggerGroup(), 0, 0);
        root.Controls.Add(_startWithWindows, 0, 1);
        root.Controls.Add(BuildDisplayGroup(), 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);

        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        var closeHost = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        closeHost.Controls.Add(close);
        root.Controls.Add(closeHost, 0, 4);

        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;

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
        _recordButton.Click += (_, _) => ToggleRecord();
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
        FormClosed += (_, _) =>
        {
            _joystick.BindingCaptured -= OnBindingCaptured;
            _joystick.DevicesChanged -= UpdateStatus;
            _switcher.StatusChanged -= OnSwitcherStatus;
            if (_joystick.IsRecording)
            {
                _joystick.StopRecord();
            }
        };

        LoadValues();
        RebuildDisplayRows();
        UpdateStatus();
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
        _startWithWindows.Checked = StartupService.IsEnabled();
        _settings.StartWithWindows = _startWithWindows.Checked;
        _suppressEvents = false;
        UpdateBindingLabel();
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
                Tag = display.DevicePath
            };
            switchButton.Click += (_, _) => _switcher.SwitchTo((string)switchButton.Tag!);

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

    private void ToggleRecord()
    {
        if (_joystick.IsRecording)
        {
            _joystick.StopRecord();
            return;
        }

        _recordButton.Text = "Stop";
        _bindingLabel.Text = _settings.TriggerKind == TriggerKind.Chord
            ? "Hold the buttons, then release…"
            : "Press the buttons in order…";
        _joystick.BeginRecord(_settings.TriggerKind);
    }

    private void OnBindingCaptured(IReadOnlyList<int> buttons)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            _settings.Buttons = buttons.ToList();
            _recordButton.Text = "Record";
            UpdateBindingLabel();
            Save();
        });
    }

    private void OnSwitcherStatus(string _) => UpdateStatus();

    private void UpdateBindingLabel()
    {
        _bindingLabel.Text = FormatBinding(_settings.TriggerKind, _settings.Buttons);
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
        SettingsStore.Save(_settings);
        _joystick.CurrentSettings = _settings;
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
