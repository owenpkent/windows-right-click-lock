using System.Runtime.InteropServices;
using WindowsMouseMods.Native;
using static WindowsMouseMods.Native.NativeMethods;

namespace WindowsMouseMods.Hooks;

internal sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly HookProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event EventHandler<KeyboardHookEventArgs>? KeyEvent;

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        var hMod = GetModuleHandle(null);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx (keyboard) failed: {Marshal.GetLastWin32Error()}");
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            var isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            var isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            var injected = (data.flags & LLKHF_INJECTED) != 0;
            var args = new KeyboardHookEventArgs((int)data.vkCode, isKeyDown, isKeyUp, injected);
            KeyEvent?.Invoke(this, args);
            if (args.Suppress)
                return 1;
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}

internal sealed class KeyboardHookEventArgs : EventArgs
{
    public int VirtualKey { get; }
    public bool IsKeyDown { get; }
    public bool IsKeyUp { get; }
    public bool Injected { get; }
    public bool Suppress { get; set; }

    public KeyboardHookEventArgs(int vk, bool keyDown, bool keyUp, bool injected)
    {
        VirtualKey = vk;
        IsKeyDown = keyDown;
        IsKeyUp = keyUp;
        Injected = injected;
    }
}
