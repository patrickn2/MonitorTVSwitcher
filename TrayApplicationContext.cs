using GameShortcut.Forms;
using GameShortcut.Models;
using GameShortcut.Services;

namespace GameShortcut;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly DeviceNotificationWindow _messageWindow;
    private readonly JoystickWatcher _joystick;
    private readonly DisplaySwitchService _switcher;
    private readonly Icon _icon;
    private readonly System.Windows.Forms.Timer _deviceDebounce;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();
        _icon = LoadIcon();

        _messageWindow = new DeviceNotificationWindow();
        _joystick = new JoystickWatcher(_messageWindow.Handle)
        {
            CurrentSettings = _settings
        };
        _switcher = new DisplaySwitchService(_joystick);

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => RebuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = AppInfo.DisplayName,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowSettings();
            }
        };

        _deviceDebounce = new System.Windows.Forms.Timer { Interval = 400 };
        _deviceDebounce.Tick += (_, _) =>
        {
            _deviceDebounce.Stop();
            _joystick.Rescan();
        };
        _messageWindow.DevicesChanged += () =>
        {
            _deviceDebounce.Stop();
            _deviceDebounce.Start();
        };

        _joystick.Triggered += () =>
        {
            if (_switcher.IsBusy)
            {
                return;
            }

            _switcher.ToggleFromJoystick();
        };
        _switcher.BusyChanged += UpdateTooltip;
        _switcher.StatusChanged += status =>
        {
            UpdateTooltip();
            ShowStatusBalloon(status);
        };

        if (StartupService.IsEnabled())
        {
            StartupService.SetEnabled(true);
        }

        _joystick.Start();
        UpdateTooltip();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        var displays = DisplayService.Enumerate();
        var current = DisplayService.GetCurrent(displays);
        bool busy = _switcher.IsBusy;

        if (displays.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("No displays detected") { Enabled = false });
        }
        else
        {
            foreach (var display in displays)
            {
                bool isCurrent = current is not null
                    && string.Equals(current.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase);
                var item = new ToolStripMenuItem(display.MenuLabel)
                {
                    Enabled = !busy && !isCurrent,
                    Checked = isCurrent
                };
                string path = display.DevicePath;
                item.Click += (_, _) => _switcher.SwitchTo(path);
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Show();
            _settingsForm.Activate();
            return;
        }

        _settings = SettingsStore.Load();
        _joystick.CurrentSettings = _settings;
        _settingsForm = new SettingsForm(_joystick, _switcher, _settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settings = SettingsStore.Load();
            _joystick.CurrentSettings = _settings;
            _settingsForm = null;
        };
        _settingsForm.Show();
    }

    private void ShowStatusBalloon(string status)
    {
        if (string.IsNullOrWhiteSpace(status) || status == "Switching display…")
        {
            return;
        }

        bool failed = status.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Could not", StringComparison.OrdinalIgnoreCase);
        _notifyIcon.BalloonTipTitle = AppInfo.DisplayName;
        _notifyIcon.BalloonTipText = status.Length > 240 ? status[..240] : status;
        _notifyIcon.BalloonTipIcon = failed ? ToolTipIcon.Error : ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void UpdateTooltip()
    {
        string text = _switcher.IsBusy
            ? $"{AppInfo.DisplayName} – Switching display…"
            : AppInfo.DisplayName;
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceDebounce.Dispose();
            _joystick.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
            _messageWindow.DestroyHandle();
            _settingsForm?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadIcon()
    {
        var assembly = typeof(TrayApplicationContext).Assembly;
        using var stream = assembly.GetManifestResourceStream("GameShortcut.Assets.tray.ico");
        if (stream is not null)
        {
            return new Icon(stream);
        }

        return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath)
            ?? SystemIcons.Application;
    }

    private sealed class DeviceNotificationWindow : NativeWindow
    {
        private const int WmDeviceChange = 0x0219;

        public DeviceNotificationWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "MonitorTVSwitcherNotify",
                Style = unchecked((int)0x80000000)
            });
        }

        public event Action? DevicesChanged;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmDeviceChange)
            {
                DevicesChanged?.Invoke();
            }

            base.WndProc(ref m);
        }
    }
}
