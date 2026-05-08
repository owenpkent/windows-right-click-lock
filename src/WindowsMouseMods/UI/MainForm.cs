using WindowsMouseMods.Core;

namespace WindowsMouseMods.UI;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly RightClickLockController _controller;
    private readonly Action _onSaved;
    private readonly Action _onExitRequested;
    private readonly Action _onShowDebug;

    private readonly CheckBox _enabledCheckbox = new() { Text = "Enabled", AutoSize = true };
    private readonly NumericUpDown _holdMs = new() { Minimum = 100, Maximum = 3000, Increment = 50, Value = 500, Width = 80 };
    private readonly CheckBox _autoStartCheckbox = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _startMinimizedCheckbox = new() { Text = "Start minimized to tray", AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true };
    private readonly Button _debugButton = new() { Text = "Debug window...", AutoSize = true };

    /// <summary>True only when the close has already been resolved via TaskDialog (Minimize/Exit).</summary>
    private bool _closeResolved;

    public MainForm(AppSettings settings, RightClickLockController controller, Action onSaved, Action onExitRequested, Action onShowDebug)
    {
        _settings = settings;
        _controller = controller;
        _onSaved = onSaved;
        _onExitRequested = onExitRequested;
        _onShowDebug = onShowDebug;

        Text = "Windows Mouse Mods";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        ClientSize = new Size(380, 260);

        BuildLayout();
        WireEvents();
        LoadFromSettings();
        UpdateStatus();
        _controller.LockStateChanged += OnLockStateChanged;
        FormClosed += (_, _) => _controller.LockStateChanged -= OnLockStateChanged;
    }

    private void OnLockStateChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(UpdateStatus);
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

        root.Controls.Add(_enabledCheckbox);

        var clickGroup = new GroupBox { Text = "ClickLock", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8) };
        var clickRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        clickRow.Controls.Add(new Label { Text = "Hold to lock (ms):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        clickRow.Controls.Add(_holdMs);
        clickGroup.Controls.Add(clickRow);
        root.Controls.Add(clickGroup);

        var startupGroup = new GroupBox { Text = "Startup", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8) };
        var startupLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        startupLayout.Controls.Add(_autoStartCheckbox);
        startupLayout.Controls.Add(_startMinimizedCheckbox);
        startupGroup.Controls.Add(startupLayout);
        root.Controls.Add(startupGroup);

        root.Controls.Add(_statusLabel);

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttonRow.Controls.Add(_closeButton);
        buttonRow.Controls.Add(_saveButton);
        // Push the debug button to the far left of the row.
        _debugButton.Margin = new Padding(0, 3, 24, 3);
        buttonRow.Controls.Add(_debugButton);
        root.Controls.Add(buttonRow);

        Controls.Add(root);
    }

    private void WireEvents()
    {
        _saveButton.Click += (_, _) => Save();
        _closeButton.Click += (_, _) => Close();
        _debugButton.Click += (_, _) => _onShowDebug();
        FormClosing += OnFormClosing;
    }

    private void LoadFromSettings()
    {
        _enabledCheckbox.Checked = _settings.Enabled;
        _holdMs.Value = Math.Clamp(_settings.ClickLockHoldMs, (int)_holdMs.Minimum, (int)_holdMs.Maximum);
        _autoStartCheckbox.Checked = AutoStart.IsEnabled();
        _startMinimizedCheckbox.Checked = _settings.StartMinimized;
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

    private void Save()
    {
        _settings.Enabled = _enabledCheckbox.Checked;
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
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Programmatic close from inside the TaskDialog handler — let it through.
        if (_closeResolved) return;

        // Windows is shutting down — let it through too.
        if (e.CloseReason != CloseReason.UserClosing) return;

        var choice = AskMinimizeOrExit();
        if (choice == CloseChoice.Cancel)
        {
            e.Cancel = true;
            return;
        }

        _closeResolved = true;
        if (choice == CloseChoice.Exit)
        {
            // Hide first so the form doesn't flash before the app tears down.
            e.Cancel = true;
            Hide();
            _onExitRequested();
        }
        else
        {
            e.Cancel = true;
            Hide();
        }
    }

    private enum CloseChoice { Minimize, Exit, Cancel }

    private CloseChoice AskMinimizeOrExit()
    {
        var minimizeButton = new TaskDialogButton("Minimize to Tray") { AllowCloseDialog = true };
        var exitButton = new TaskDialogButton("Exit") { AllowCloseDialog = true };
        var cancelButton = TaskDialogButton.Cancel;

        var page = new TaskDialogPage
        {
            Caption = "Windows Mouse Mods",
            Heading = "Minimize to tray, or exit?",
            Text = "Minimize keeps the app running in the system tray. Exit quits completely.",
            Icon = TaskDialogIcon.Information,
            DefaultButton = minimizeButton,
            Buttons = { minimizeButton, exitButton, cancelButton },
            AllowCancel = true,
        };

        var result = TaskDialog.ShowDialog(this, page);
        if (result == minimizeButton) return CloseChoice.Minimize;
        if (result == exitButton) return CloseChoice.Exit;
        return CloseChoice.Cancel;
    }
}
