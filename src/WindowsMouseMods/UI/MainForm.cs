using WindowsMouseMods.Core;

namespace WindowsMouseMods.UI;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly RightClickLockController _controller;
    private readonly Action _onSaved;

    private readonly CheckBox _enabledCheckbox = new() { Text = "Enabled", AutoSize = true };
    private readonly RadioButton _hotkeyRadio = new() { Text = "Hotkey toggle", AutoSize = true };
    private readonly RadioButton _clickLockRadio = new() { Text = "ClickLock (press &amp; hold RMB)", AutoSize = true };
    private readonly TextBox _hotkeyDisplay = new() { ReadOnly = true, Width = 140 };
    private readonly Button _hotkeyCaptureButton = new() { Text = "Set hotkey...", AutoSize = true };
    private readonly Label _hotkeyHint = new() { Text = "Press any key. Esc cancels.", ForeColor = SystemColors.GrayText, AutoSize = true, Visible = false };
    private readonly NumericUpDown _holdMs = new() { Minimum = 100, Maximum = 3000, Increment = 50, Value = 500, Width = 80 };
    private readonly CheckBox _autoStartCheckbox = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _startMinimizedCheckbox = new() { Text = "Start minimized to tray", AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true };

    private bool _capturingHotkey;
    private int _pendingHotkeyVk;

    public MainForm(AppSettings settings, RightClickLockController controller, Action onSaved)
    {
        _settings = settings;
        _controller = controller;
        _onSaved = onSaved;
        _pendingHotkeyVk = settings.HotkeyVirtualKey;

        Text = "Windows Mouse Mods";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        ClientSize = new Size(420, 360);
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        LoadFromSettings();
        UpdateStatus();
        _controller.LockStateChanged += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(UpdateStatus);
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Enabled
        root.Controls.Add(_enabledCheckbox);

        // Mode group
        var modeGroup = new GroupBox { Text = "Mode", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8) };
        var modeLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        modeLayout.Controls.Add(_hotkeyRadio);
        modeLayout.Controls.Add(_clickLockRadio);
        modeGroup.Controls.Add(modeLayout);
        root.Controls.Add(modeGroup);

        // Hotkey row
        var hotkeyGroup = new GroupBox { Text = "Hotkey", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8) };
        var hotkeyRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        hotkeyRow.Controls.Add(new Label { Text = "Key:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        hotkeyRow.Controls.Add(_hotkeyDisplay);
        hotkeyRow.Controls.Add(_hotkeyCaptureButton);
        hotkeyRow.Controls.Add(_hotkeyHint);
        hotkeyGroup.Controls.Add(hotkeyRow);
        root.Controls.Add(hotkeyGroup);

        // ClickLock hold ms
        var clickGroup = new GroupBox { Text = "ClickLock", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8) };
        var clickRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        clickRow.Controls.Add(new Label { Text = "Hold to lock (ms):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        clickRow.Controls.Add(_holdMs);
        clickGroup.Controls.Add(clickRow);
        root.Controls.Add(clickGroup);

        // Startup
        var startupGroup = new GroupBox { Text = "Startup", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8) };
        var startupLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        startupLayout.Controls.Add(_autoStartCheckbox);
        startupLayout.Controls.Add(_startMinimizedCheckbox);
        startupGroup.Controls.Add(startupLayout);
        root.Controls.Add(startupGroup);

        // Status + buttons
        root.Controls.Add(_statusLabel);

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttonRow.Controls.Add(_closeButton);
        buttonRow.Controls.Add(_saveButton);
        root.Controls.Add(buttonRow);

        Controls.Add(root);
    }

    private void WireEvents()
    {
        _hotkeyCaptureButton.Click += (_, _) => StartHotkeyCapture();
        _saveButton.Click += (_, _) => SaveAndClose();
        _closeButton.Click += (_, _) => Hide();
        FormClosing += OnFormClosing;
        KeyDown += OnFormKeyDown;
    }

    private void LoadFromSettings()
    {
        _enabledCheckbox.Checked = _settings.Enabled;
        _hotkeyRadio.Checked = _settings.Mode == LockMode.HotkeyToggle;
        _clickLockRadio.Checked = _settings.Mode == LockMode.ClickLock;
        _holdMs.Value = Math.Clamp(_settings.ClickLockHoldMs, (int)_holdMs.Minimum, (int)_holdMs.Maximum);
        _autoStartCheckbox.Checked = AutoStart.IsEnabled();
        _startMinimizedCheckbox.Checked = _settings.StartMinimized;
        _hotkeyDisplay.Text = HotkeyName(_settings.HotkeyVirtualKey);
    }

    private void UpdateStatus()
    {
        if (!_settings.Enabled)
            _statusLabel.Text = "Status: disabled";
        else if (_controller.Locked)
            _statusLabel.Text = "Status: RIGHT BUTTON LOCKED";
        else
            _statusLabel.Text = "Status: ready";
    }

    private void StartHotkeyCapture()
    {
        _capturingHotkey = true;
        _hotkeyHint.Visible = true;
        _hotkeyCaptureButton.Text = "Press a key...";
        _hotkeyCaptureButton.Enabled = false;
        Focus();
    }

    private void EndHotkeyCapture(bool committed, int vk)
    {
        _capturingHotkey = false;
        _hotkeyHint.Visible = false;
        _hotkeyCaptureButton.Text = "Set hotkey...";
        _hotkeyCaptureButton.Enabled = true;
        if (committed)
        {
            _pendingHotkeyVk = vk;
            _hotkeyDisplay.Text = HotkeyName(vk);
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            EndHotkeyCapture(committed: false, vk: 0);
            return;
        }

        // Ignore lone modifier keys.
        var k = e.KeyCode;
        if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu ||
            k == Keys.LControlKey || k == Keys.RControlKey ||
            k == Keys.LShiftKey || k == Keys.RShiftKey ||
            k == Keys.LMenu || k == Keys.RMenu ||
            k == Keys.LWin || k == Keys.RWin)
        {
            return;
        }

        EndHotkeyCapture(committed: true, vk: (int)k);
    }

    private static string HotkeyName(int vk)
    {
        var keys = (Keys)vk;
        return keys.ToString();
    }

    private void SaveAndClose()
    {
        _settings.Enabled = _enabledCheckbox.Checked;
        _settings.Mode = _hotkeyRadio.Checked ? LockMode.HotkeyToggle : LockMode.ClickLock;
        _settings.HotkeyVirtualKey = _pendingHotkeyVk;
        _settings.ClickLockHoldMs = (int)_holdMs.Value;
        _settings.StartMinimized = _startMinimizedCheckbox.Checked;
        _settings.AutoStartWithWindows = _autoStartCheckbox.Checked;

        try
        {
            AutoStart.SetEnabled(_autoStartCheckbox.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't update Run key: {ex.Message}", "Windows Mouse Mods",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _settings.Save();
        _onSaved();
        UpdateStatus();
        Hide();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Close button on the title bar should hide to tray, not exit.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
