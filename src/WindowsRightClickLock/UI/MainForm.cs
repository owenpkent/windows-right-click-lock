using WindowsRightClickLock.Core;

namespace WindowsRightClickLock.UI;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly RightClickLockController _controller;
    private readonly Action _onSaved;
    private readonly Action _onShowDebug;

    private static readonly int[] HoldMsSteps = { 100, 200, 300, 400, 500, 700, 900, 1200, 1500, 2000 };

    private readonly CheckBox _enabledCheckbox = new() { Text = "Enabled", AutoSize = true };
    private readonly TrackBar _holdSlider = new()
    {
        Minimum = 0,
        Maximum = HoldMsSteps.Length - 1,
        TickFrequency = 1,
        TickStyle = TickStyle.BottomRight,
        SmallChange = 1,
        LargeChange = 1,
        Width = 220,
        AutoSize = false,
        Height = 36,
    };
    private readonly Label _holdValueLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 4) };
    private readonly CheckBox _moveCancelCheckbox = new() { Text = "Cancel arming if mouse moves during hold", AutoSize = true };
    private readonly NumericUpDown _moveCancelPx = new() { Minimum = 1, Maximum = 50, Increment = 1, Value = 5, Width = 60 };
    private readonly CheckBox _autoStartCheckbox = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _startMinimizedCheckbox = new() { Text = "Start minimized to tray", AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true };
    private readonly Button _debugButton = new() { Text = "Debug window...", AutoSize = true };

    public MainForm(AppSettings settings, RightClickLockController controller, Action onSaved, Action onShowDebug)
    {
        _settings = settings;
        _controller = controller;
        _onSaved = onSaved;
        _onShowDebug = onShowDebug;

        Text = "Windows Right-Click Lock";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(380, 0);

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
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
        };

        _enabledCheckbox.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_enabledCheckbox);

        var clickGroup = new GroupBox { Text = "ClickLock", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 8) };
        var clickContent = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        var holdHeaderRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        holdHeaderRow.Controls.Add(new Label { Text = "Hold to lock:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        holdHeaderRow.Controls.Add(_holdValueLabel);
        clickContent.Controls.Add(holdHeaderRow);

        var holdSliderRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        holdSliderRow.Controls.Add(new Label { Text = "Short", AutoSize = true, Margin = new Padding(0, 10, 4, 0) });
        holdSliderRow.Controls.Add(_holdSlider);
        holdSliderRow.Controls.Add(new Label { Text = "Long", AutoSize = true, Margin = new Padding(4, 10, 0, 0) });
        clickContent.Controls.Add(holdSliderRow);

        clickContent.Controls.Add(_moveCancelCheckbox);

        var moveThresholdRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        moveThresholdRow.Controls.Add(new Label { Text = "Movement threshold (px):", AutoSize = true, Margin = new Padding(16, 6, 6, 0) });
        moveThresholdRow.Controls.Add(_moveCancelPx);
        clickContent.Controls.Add(moveThresholdRow);

        clickGroup.Controls.Add(clickContent);
        root.Controls.Add(clickGroup);

        var startupGroup = new GroupBox { Text = "Startup", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 8) };
        var startupLayout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        startupLayout.Controls.Add(_autoStartCheckbox);
        startupLayout.Controls.Add(_startMinimizedCheckbox);
        startupGroup.Controls.Add(startupLayout);
        root.Controls.Add(startupGroup);

        _statusLabel.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_statusLabel);

        var buttonRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
        buttonRow.Controls.Add(_closeButton);
        buttonRow.Controls.Add(_saveButton);
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
        _moveCancelCheckbox.CheckedChanged += (_, _) => _moveCancelPx.Enabled = _moveCancelCheckbox.Checked;
        _holdSlider.ValueChanged += (_, _) => UpdateHoldValueLabel();
        FormClosing += OnFormClosing;
    }

    private void LoadFromSettings()
    {
        _enabledCheckbox.Checked = _settings.Enabled;
        _holdSlider.Value = NearestHoldStepIndex(_settings.ClickLockHoldMs);
        UpdateHoldValueLabel();
        _moveCancelCheckbox.Checked = _settings.MoveCancelEnabled;
        _moveCancelPx.Value = Math.Clamp(_settings.MoveCancelPixels, (int)_moveCancelPx.Minimum, (int)_moveCancelPx.Maximum);
        _moveCancelPx.Enabled = _moveCancelCheckbox.Checked;
        _autoStartCheckbox.Checked = AutoStart.IsEnabled();
        _startMinimizedCheckbox.Checked = _settings.StartMinimized;
    }

    private void UpdateHoldValueLabel()
    {
        _holdValueLabel.Text = $"{HoldMsSteps[_holdSlider.Value]} ms";
    }

    private static int NearestHoldStepIndex(int ms)
    {
        int bestIndex = 0;
        int bestDelta = int.MaxValue;
        for (int i = 0; i < HoldMsSteps.Length; i++)
        {
            int delta = Math.Abs(HoldMsSteps[i] - ms);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIndex = i;
            }
        }
        return bestIndex;
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
        _settings.ClickLockHoldMs = HoldMsSteps[_holdSlider.Value];
        _settings.MoveCancelEnabled = _moveCancelCheckbox.Checked;
        _settings.MoveCancelPixels = (int)_moveCancelPx.Value;
        _settings.StartMinimized = _startMinimizedCheckbox.Checked;
        _settings.AutoStartWithWindows = _autoStartCheckbox.Checked;

        try
        {
            AutoStart.SetEnabled(_autoStartCheckbox.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't update Run key: {ex.Message}", "Windows Right-Click Lock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _settings.Save();
        _onSaved();
        UpdateStatus();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // System shutdown / programmatic dispose: let it through.
        if (e.CloseReason != CloseReason.UserClosing) return;

        // User clicked X or Close: just hide. Exit lives on the tray menu.
        e.Cancel = true;
        Hide();
    }
}
