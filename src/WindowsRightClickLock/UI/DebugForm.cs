using WindowsRightClickLock.Core;

namespace WindowsRightClickLock.UI;

internal sealed class DebugForm : Form
{
    private const int MaxLines = 1000;

    private readonly RightClickLockController _controller;
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f),
        BackColor = Color.Black,
        ForeColor = Color.LimeGreen,
    };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(8, 4, 8, 4), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _pauseButton = new() { Text = "Pause", AutoSize = true, Margin = new Padding(4) };
    private readonly Button _clearButton = new() { Text = "Clear", AutoSize = true, Margin = new Padding(4) };
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", Checked = true, AutoSize = true, Margin = new Padding(4, 8, 4, 4) };

    private bool _paused;

    public DebugForm(RightClickLockController controller)
    {
        _controller = controller;

        Text = "Windows Right-Click Lock - Debug";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 400);
        MinimumSize = new Size(420, 240);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildLayout();

        _pauseButton.Click += (_, _) => TogglePause();
        _clearButton.Click += (_, _) => _log.Clear();

        _controller.DebugMessage += OnDebugMessage;
        _controller.LockStateChanged += OnLockStateChanged;
        FormClosed += (_, _) =>
        {
            _controller.DebugMessage -= OnDebugMessage;
            _controller.LockStateChanged -= OnLockStateChanged;
        };

        UpdateStatus();
        AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Debug window opened. Recent events will appear below.");
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var statusPanel = new Panel { Dock = DockStyle.Fill, Height = 28, BackColor = SystemColors.Control };
        statusPanel.Controls.Add(_status);
        root.Controls.Add(statusPanel, 0, 0);

        root.Controls.Add(_log, 0, 1);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(4),
        };
        buttonRow.Controls.Add(_pauseButton);
        buttonRow.Controls.Add(_clearButton);
        buttonRow.Controls.Add(_autoScroll);
        root.Controls.Add(buttonRow, 0, 2);

        Controls.Add(root);
    }

    private void OnDebugMessage(object? sender, string message)
    {
        if (_paused) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(() => AppendLine(line));
    }

    private void OnLockStateChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(UpdateStatus);
    }

    private void AppendLine(string line)
    {
        // Trim oldest lines to keep memory bounded.
        if (_log.Lines.Length >= MaxLines)
        {
            var trimmed = _log.Lines.Skip(_log.Lines.Length - MaxLines + 1).ToArray();
            _log.Lines = trimmed;
        }

        _log.AppendText(line + Environment.NewLine);

        if (_autoScroll.Checked)
        {
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseButton.Text = _paused ? "Resume" : "Pause";
        AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] -- {(_paused ? "paused" : "resumed")} --");
    }

    private void UpdateStatus()
    {
        var enabled = _controller.Settings.Enabled ? "enabled" : "DISABLED";
        var lockState = _controller.Locked ? "RIGHT BUTTON LOCKED" : "ready";
        _status.Text = $"State: {enabled}  |  {lockState}  |  hold = {_controller.Settings.ClickLockHoldMs} ms";
    }
}
