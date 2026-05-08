using WindowsMouseMods.Hooks;
using WindowsMouseMods.Native;
using static WindowsMouseMods.Native.NativeMethods;

namespace WindowsMouseMods.Core;

/// <summary>
/// Coordinates the mouse and keyboard hooks to implement two RMB-lock modes:
///
///   HotkeyToggle: a configured key flips lock state on/off. Synthetic RMB DOWN/UP is injected.
///   ClickLock:    if the user holds RMB longer than the threshold and releases, the UP is
///                 suppressed and the system continues to see RMB held. The next physical
///                 RMB tap releases the lock (and is itself swallowed).
///
/// In both modes, while locked, a fresh physical RMB DOWN-UP releases the lock.
/// </summary>
internal sealed class RightClickLockController : IDisposable
{
    private readonly LowLevelMouseHook _mouseHook = new();
    private readonly LowLevelKeyboardHook _keyboardHook = new();
    private readonly System.Windows.Forms.Timer _clickLockTimer;

    private AppSettings _settings;
    private bool _locked;
    private bool _physicalRmbDown;
    private bool _clickLockArmed;
    private bool _swallowNextRealRmbUp;
    private int _hotkeyDownVk;

    public bool Locked => _locked;
    public AppSettings Settings => _settings;

    public event EventHandler? LockStateChanged;

    public RightClickLockController(AppSettings settings)
    {
        _settings = settings;
        _clickLockTimer = new System.Windows.Forms.Timer { Interval = Math.Max(50, settings.ClickLockHoldMs) };
        _clickLockTimer.Tick += OnClickLockTimerTick;

        _mouseHook.MouseEvent += OnMouseEvent;
        _keyboardHook.KeyEvent += OnKeyEvent;
    }

    public void Start()
    {
        _mouseHook.Install();
        _keyboardHook.Install();
    }

    public void Stop()
    {
        ReleaseLockIfHeld();
        _mouseHook.Uninstall();
        _keyboardHook.Uninstall();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _clickLockTimer.Interval = Math.Max(50, settings.ClickLockHoldMs);
        if (!settings.Enabled)
            ReleaseLockIfHeld();
    }

    private void OnClickLockTimerTick(object? sender, EventArgs e)
    {
        _clickLockTimer.Stop();
        if (_physicalRmbDown && _settings.Enabled && _settings.Mode == LockMode.ClickLock)
            _clickLockArmed = true;
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
            ReleaseLockIfHeld();
            return;
        }

        _physicalRmbDown = true;
        _clickLockArmed = false;
        if (_settings.Mode == LockMode.ClickLock)
        {
            _clickLockTimer.Stop();
            _clickLockTimer.Interval = Math.Max(50, _settings.ClickLockHoldMs);
            _clickLockTimer.Start();
        }
    }

    private void HandlePhysicalRmbUp(MouseHookEventArgs e)
    {
        if (_swallowNextRealRmbUp)
        {
            _swallowNextRealRmbUp = false;
            e.Suppress = true;
            return;
        }

        _physicalRmbDown = false;
        _clickLockTimer.Stop();

        if (_settings.Mode == LockMode.ClickLock && _clickLockArmed)
        {
            // Suppress the UP — system now believes the button is still held.
            _clickLockArmed = false;
            e.Suppress = true;
            _locked = true;
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnKeyEvent(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Injected) return;
        if (!_settings.Enabled) return;
        if (_settings.Mode != LockMode.HotkeyToggle) return;
        if (e.VirtualKey != _settings.HotkeyVirtualKey) return;

        if (e.IsKeyDown)
        {
            // Suppress the hotkey at the system level so it doesn't bleed into the focused app.
            e.Suppress = true;
            if (_hotkeyDownVk == e.VirtualKey) return; // ignore autorepeat
            _hotkeyDownVk = e.VirtualKey;
            ToggleLock();
        }
        else if (e.IsKeyUp)
        {
            e.Suppress = true;
            if (_hotkeyDownVk == e.VirtualKey)
                _hotkeyDownVk = 0;
        }
    }

    private void ToggleLock()
    {
        if (_locked)
            ReleaseLockIfHeld();
        else
        {
            InputInjector.RightDown();
            _locked = true;
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReleaseLockIfHeld()
    {
        if (!_locked) return;
        InputInjector.RightUp();
        _locked = false;
        LockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Stop();
        _clickLockTimer.Dispose();
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
    }
}
