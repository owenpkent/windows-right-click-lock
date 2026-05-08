using WindowsMouseMods.Core;
using WindowsMouseMods.Native;

namespace WindowsMouseMods.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly RightClickLockController _controller;
    private readonly AppSettings _settings;
    private readonly Icon _iconIdle;
    private readonly Icon _iconLocked;
    private MainForm? _mainForm;
    private DebugForm? _debugForm;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _debugItem;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _controller = new RightClickLockController(_settings);
        _controller.LockStateChanged += (_, _) => UpdateTrayIcon();

        _iconIdle = TrayIcons.CreateIdle();
        _iconLocked = TrayIcons.CreateLocked();

        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

        _enabledItem = new ToolStripMenuItem("Enabled", null, OnToggleEnabled) { Checked = _settings.Enabled, CheckOnClick = true };
        _debugItem = new ToolStripMenuItem("Show debug window", null, (_, _) => ToggleDebugWindow()) { CheckOnClick = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowMainForm());
        menu.Items.Add(_debugItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = _iconIdle,
            Visible = true,
            Text = "Windows Mouse Mods",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainForm();

        try
        {
            _controller.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to install hooks:\n{ex.Message}", "Windows Mouse Mods",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateTrayIcon();

        if (_settings.ShowDebugOnStartup)
            ShowDebugWindow();

        if (!_settings.StartMinimized)
            ShowMainForm();
    }

    private void OnToggleEnabled(object? sender, EventArgs e)
    {
        _settings.Enabled = _enabledItem.Checked;
        _controller.ApplySettings(_settings);
        _settings.Save();
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        var status = !_settings.Enabled ? "disabled"
            : _controller.Locked ? "locked"
            : "ready";
        _trayIcon.Text = $"Windows Mouse Mods — {status}";
        _trayIcon.Icon = _controller.Locked ? _iconLocked : _iconIdle;
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        // Releasing the lock when the workstation locks prevents returning from the lock
        // screen with RMB still synthetically held. SessionLogoff covers logout.
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock ||
            e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLogoff)
        {
            InputInjector.EmergencyRelease();
        }
    }

    public void ShowMainForm()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_settings, _controller, OnSettingsSaved, ExitApplication, ShowDebugWindow);
            _mainForm.FormClosed += (_, _) => _mainForm = null;
        }
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.BringToFront();
        _mainForm.Activate();
    }

    public void ShowDebugWindow()
    {
        if (_debugForm == null || _debugForm.IsDisposed)
        {
            _debugForm = new DebugForm(_controller);
            _debugForm.FormClosed += (_, _) =>
            {
                _debugForm = null;
                _debugItem.Checked = false;
                if (_settings.ShowDebugOnStartup)
                {
                    _settings.ShowDebugOnStartup = false;
                    _settings.Save();
                }
            };
        }
        _debugForm.Show();
        _debugForm.WindowState = FormWindowState.Normal;
        _debugForm.BringToFront();
        _debugForm.Activate();
        _debugItem.Checked = true;

        if (!_settings.ShowDebugOnStartup)
        {
            _settings.ShowDebugOnStartup = true;
            _settings.Save();
        }
    }

    private void ToggleDebugWindow()
    {
        if (_debugForm != null && !_debugForm.IsDisposed)
        {
            _debugForm.Close();
        }
        else
        {
            ShowDebugWindow();
        }
    }

    private void OnSettingsSaved()
    {
        _enabledItem.Checked = _settings.Enabled;
        _controller.ApplySettings(_settings);
        UpdateTrayIcon();
    }

    public void ExitApplication()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        _controller.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _iconIdle.Dispose();
        _iconLocked.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            _controller.Dispose();
            _trayIcon.Dispose();
            _iconIdle.Dispose();
            _iconLocked.Dispose();
        }
        base.Dispose(disposing);
    }
}
