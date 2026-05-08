using System.Runtime.InteropServices;
using WindowsRightClickLock.Native;
using static WindowsRightClickLock.Native.NativeMethods;

namespace WindowsRightClickLock.Hooks;

/// <summary>
/// Wraps WH_MOUSE_LL. Raise the event for every event; subscribers can mark e.Suppress = true
/// to drop the message before it reaches the rest of the system.
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private const int MaxLoggedErrors = 5;
    private static int _errorCount;
    private static readonly string ErrorLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsRightClickLock", "hook-errors.log");

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
        // Never let an exception escape the hook proc; Windows will silently
        // unhook us and the app loses its core functionality with no visible failure.
        try
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
        }
        catch (Exception ex)
        {
            // Never let an exception escape the hook proc; Windows will silently unhook us
            // and the user has no way to recover other than restarting the app. We also can't
            // surface the exception inline (no UI thread guarantee, can't block the hook),
            // so capture up to the first MaxLoggedErrors instances asynchronously to a file
            // under %LocalAppData%\WindowsRightClickLock\hook-errors.log. See docs/security-review.md (M1).
            LogHookErrorAsync(ex);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void LogHookErrorAsync(Exception ex)
    {
        if (Interlocked.Increment(ref _errorCount) > MaxLoggedErrors) return;
        var snapshot = $"[{DateTime.Now:O}] {ex}{Environment.NewLine}";
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
                File.AppendAllText(ErrorLogPath, snapshot);
            }
            catch
            {
                // Best-effort. If we can't write the log we have no other recourse.
            }
        });
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
