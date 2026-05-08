using System.Runtime.InteropServices;
using WindowsMouseMods.Native;
using static WindowsMouseMods.Native.NativeMethods;

namespace WindowsMouseMods.Hooks;

/// <summary>
/// Wraps WH_MOUSE_LL. Raise the event for every event; subscribers can mark e.Suppress = true
/// to drop the message before it reaches the rest of the system.
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private readonly HookProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event EventHandler<MouseHookEventArgs>? MouseEvent;

    public LowLevelMouseHook()
    {
        // Keep delegate alive for the lifetime of the hook.
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        var hMod = GetModuleHandle(null);
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx (mouse) failed: {Marshal.GetLastWin32Error()}");
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
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var injectedByUs = data.dwExtraInfo == InjectionTag;
            var args = new MouseHookEventArgs(wParam.ToInt32(), data, injectedByUs);
            MouseEvent?.Invoke(this, args);
            if (args.Suppress)
                return 1;
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}

internal sealed class MouseHookEventArgs : EventArgs
{
    public int Message { get; }
    public MSLLHOOKSTRUCT Data { get; }
    public bool InjectedByUs { get; }
    public bool Suppress { get; set; }

    public MouseHookEventArgs(int message, MSLLHOOKSTRUCT data, bool injectedByUs)
    {
        Message = message;
        Data = data;
        InjectedByUs = injectedByUs;
    }
}
