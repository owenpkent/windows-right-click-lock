using WindowsRightClickLock.Hooks;
using WindowsRightClickLock.Native;
using static WindowsRightClickLock.Native.NativeMethods;

namespace WindowsRightClickLock.Core;

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
    private bool _moveCancelled;
    private NativeMethods.POINT _rmbDownPos;

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
            case WM_MOUSEMOVE:
                HandlePhysicalMove(e);
                break;
        }
    }

    private void HandlePhysicalMove(MouseHookEventArgs e)
    {
        // Only relevant during the brief arming window: RMB physically held, timer still running,
        // not yet armed, not already cancelled by an earlier move.
        if (!_physicalRmbDown || _clickLockArmed || _moveCancelled) return;
        if (!_settings.MoveCancelEnabled) return;

        var dx = e.Data.pt.x - _rmbDownPos.x;
        var dy = e.Data.pt.y - _rmbDownPos.y;
        var distSq = dx * dx + dy * dy;
        var threshold = _settings.MoveCancelPixels;
        if (distSq > threshold * threshold)
        {
            _moveCancelled = true;
            _clickLockTimer.Stop();
            Log($"Move-cancel: cursor moved past {threshold} px during hold; arming cancelled.");
        }
    }

    private void HandlePhysicalRmbDown(MouseHookEventArgs e)
    {
        if (_locked)
        {
            // Tap-to-release: try to send the synthetic UP first. Only suppress the physical
            // DOWN and arm the UP-swallow flag if the OS accepted our release. If SendInput
            // fails, we let the physical DOWN/UP through so the OS has a chance to correct
            // its belief about the button state. See docs/security-review.md (M6).
            if (TryReleaseLock("tap-to-release"))
            {
                e.Suppress = true;
                _swallowNextRealRmbUp = true;
                Log("Physical RMB DOWN while locked -> releasing (suppress DOWN, swallow next UP).");
            }
            else
            {
                Log("Physical RMB DOWN while locked -> SendInput failed; passing physical events through.");
            }
            return;
        }

        _physicalRmbDown = true;
        _clickLockArmed = false;
        _moveCancelled = false;
        _rmbDownPos = e.Data.pt;
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

    /// <summary>
    /// Tries to release the lock by sending a synthetic RMB UP. Returns true only if the
    /// OS accepted the event. On failure the lock state is left intact so callers can
    /// retry or fall back to passing physical events through.
    /// </summary>
    private bool TryReleaseLock(string reason)
    {
        if (!_locked) return true;
        if (!InputInjector.RightUp())
        {
            Log($"Lock release failed ({reason}): SendInput returned 0.");
            return false;
        }
        _locked = false;
        Log($"Lock released ({reason}).");
        LockStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Log(string message) => DebugMessage?.Invoke(this, message);

    public void Dispose()
    {
        Stop();
        _clickLockTimer.Dispose();
        _mouseHook.Dispose();
    }
}
