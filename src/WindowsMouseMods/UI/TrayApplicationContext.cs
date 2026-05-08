using WindowsMouseMods.Core;

namespace WindowsMouseMods.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly RightClickLockController _controller;
    private readonly AppSettings _settings;
    private MainForm? _mainForm;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _modeHotkeyItem;
    private readonly ToolStripMenuItem _modeClickLockItem;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _controller = new RightClickLockController(_settings);
        _controller.LockStateChanged += (_, _) => UpdateTrayIcon();

        _enabledItem = new ToolStripMenuItem("Enabled", null, OnToggleEnabled) { Checked = _settings.Enabled, CheckOnClick = true };
        _modeHotkeyItem = new ToolStripMenuItem("Hotkey toggle", null, (_, _) => SetMode(LockMode.HotkeyToggle));
        _modeClickLockItem = new ToolStripMenuItem("ClickLock", null, (_, _) => SetMode(LockMode.ClickLock));
        UpdateModeChecks();

        var modeMenu = new ToolStripMenuItem("Mode");
        modeMenu.DropDownItems.AddRange(new ToolStripItem[] { _modeHotkeyItem, _modeClickLockItem });

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(modeMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowMainForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
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

    private void SetMode(LockMode mode)
    {
        _settings.Mode = mode;
        UpdateModeChecks();
        _controller.ApplySettings(_settings);
        _settings.Save();
    }

    private void UpdateModeChecks()
    {
        _modeHotkeyItem.Checked = _settings.Mode == LockMode.HotkeyToggle;
        _modeClickLockItem.Checked = _settings.Mode == LockMode.ClickLock;
    }

    private void UpdateTrayIcon()
    {
        var status = !_settings.Enabled ? "disabled"
            : _controller.Locked ? "locked"
            : "ready";
        _trayIcon.Text = $"Windows Mouse Mods — {status}";
    }

    private void ShowMainForm()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_settings, _controller, OnSettingsSaved);
            _mainForm.FormClosed += (_, _) => _mainForm = null;
        }
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.BringToFront();
        _mainForm.Activate();
    }

    private void OnSettingsSaved()
    {
        _enabledItem.Checked = _settings.Enabled;
        UpdateModeChecks();
        _controller.ApplySettings(_settings);
        UpdateTrayIcon();
    }

    private void ExitApplication()
    {
        _controller.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
