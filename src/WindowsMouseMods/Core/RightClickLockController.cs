using WindowsMouseMods.Hooks;
using WindowsMouseMods.Native;
using static WindowsMouseMods.Native.NativeMethods;

namespace WindowsMouseMods.Core;

/// <summary>
/// ClickLock for the right mouse button.
///
/// Hold RMB longer than the threshold and release: the up event is suppressed and the system
/// keeps thinking RMB is held. The next physical RMB tap releases the lock (and is itself
/// swallowed cleanly so the OS only sees the synthetic up).
/// </summary>
internal sealed class RightClickLockController : IDisposable
{
    private readonly LowLevelMouseHook _mouseHook = new();
    private readonly System.Windows.Forms.Timer _clickLockTimer;

    private AppSettings _settings;
    private bool _locked;
    private bool _physicalRmbDown;
    private bool _clickLockArmed;
    private bool _swallowNextRealRmbUp;

    public bool Locked => _locked;
    public AppSettings Settings => _settings;

    public event EventHandler? LockStateChanged;
    public event EventHandler<string>? DebugMessage;

    public RightClickLockController(AppSettings settings)
    {
        _settings = settings;
        _clickLockTimer = new System.Windows.Forms.Timer { Interval = Math.Max(50, settings.ClickLockHoldMs) };
        _clickLockTimer.Tick += OnClickLockTimerTick;

        _mouseHook.MouseEvent += OnMouseEvent;
    }

    public void Start()
    {
        _mouseHook.Install();
        Log("Mouse hook installed.");
    }

    public void Stop()
    {
        ReleaseLockIfHeld("controller stopping");
        _mouseHook.Uninstall();
        Log("Mouse hook uninstalled.");
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _clickLockTimer.Interval = Math.Max(50, settings.ClickLockHoldMs);
        Log($"Settings applied: enabled={settings.Enabled}, holdMs={settings.ClickLockHoldMs}.");
        if (!settings.Enabled)
            ReleaseLockIfHeld("disabled in settings");
    }

    private void OnClickLockTimerTick(object? sender, EventArgs e)
    {
        _clickLockTimer.Stop();
        if (_physicalRmbDown && _settings.Enabled)
        {
            _clickLockArmed = true;
            Log($"ClickLock armed after {_settings.ClickLockHoldMs} ms hold.");
        }
    }

    private void OnMouseEvent(object? sender, MouseHookEventArgs e)
    {
        if (e.InjectedByUs) return;
        if (!_settings.Enabled) return;

        switch (e.Message)
        {
            case WM_RBUTTONDOWN:
                HandlePhysicalRmbDown(e);
                break;
            case WM_RBUTTONUP:
                HandlePhysicalRmbUp(e);
                break;
        }
    }

    private void HandlePhysicalRmbDown(MouseHookEventArgs e)
    {
        if (_locked)
        {
            // Tap-to-release: suppress this DOWN, also swallow the matching UP, send a real UP
            // to free the synthetic-held button.
            e.Suppress = true;
            _swallowNextRealRmbUp = true;
            Log("Physical RMB DOWN while locked -> releasing (suppress DOWN, swallow next UP).");
            ReleaseLockIfHeld("tap-to-release");
            return;
        }

        _physicalRmbDown = true;
        _clickLockArmed = false;
        _clickLockTimer.Stop();
        _clickLockTimer.Interval = Math.Max(50, _settings.ClickLockHoldMs);
        _clickLockTimer.Start();
        Log("Physical RMB DOWN -> hold timer started.");
    }

    private void HandlePhysicalRmbUp(MouseHookEventArgs e)
    {
        if (_swallowNextRealRmbUp)
        {
            _swallowNextRealRmbUp = false;
            e.Suppress = true;
            Log("Physical RMB UP swallowed (paired with release tap).");
            return;
        }

        _physicalRmbDown = false;
        _clickLockTimer.Stop();

        if (_clickLockArmed)
        {
            _clickLockArmed = false;
            e.Suppress = true;
            _locked = true;
            // The OS still thinks RMB is held because we suppressed the UP. Mirror that
            // in the held-flag so Program's emergency-release fires RBUTTON_UP if we crash.
            InputInjector.MarkHeld(true);
            Log("Physical RMB UP after threshold -> LOCKED (UP suppressed).");
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Log("Physical RMB UP before threshold -> regular click.");
        }
    }

    private void ReleaseLockIfHeld(string reason)
    {
        if (!_locked) return;
        InputInjector.RightUp();
        _locked = false;
        Log($"Lock released ({reason}).");
        LockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Log(string message) => DebugMessage?.Invoke(this, message);

    public void Dispose()
    {
        Stop();
        _clickLockTimer.Dispose();
        _mouseHook.Dispose();
    }
}
