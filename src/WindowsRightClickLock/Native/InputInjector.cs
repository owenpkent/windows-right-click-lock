using static WindowsRightClickLock.Native.NativeMethods;

namespace WindowsRightClickLock.Native;

internal static class InputInjector
{
    /// <summary>
    /// Mirrors the controller's "we believe RMB is currently held synthetically" state.
    /// Set whenever the controller engages or releases the lock. Read by Program's
    /// emergency-release path on unhandled exception or process exit so a crash
    /// doesn't leave the OS thinking RMB is permanently down.
    /// </summary>
    private static int _heldFlag;

    public static bool IsHeld => Volatile.Read(ref _heldFlag) != 0;

    public static void MarkHeld(bool held) => Volatile.Write(ref _heldFlag, held ? 1 : 0);

    /// <returns>true if SendInput accepted the event; false if it failed (e.g. UIPI block).</returns>
    public static bool RightDown()
    {
        if (!SendMouseFlag(MOUSEEVENTF_RIGHTDOWN)) return false;
        MarkHeld(true);
        return true;
    }

    /// <returns>true if SendInput accepted the event; false if it failed.</returns>
    public static bool RightUp()
    {
        if (!SendMouseFlag(MOUSEEVENTF_RIGHTUP)) return false;
        MarkHeld(false);
        return true;
    }

    /// <summary>Best-effort release for use from finalizer / unhandled-exception paths.</summary>
    public static void EmergencyRelease()
    {
        if (!IsHeld) return;
        try
        {
            if (SendMouseFlag(MOUSEEVENTF_RIGHTUP))
                MarkHeld(false);
        }
        catch { /* swallow; we're already on a failure path */ }
    }

    private static bool SendMouseFlag(uint flag)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi = new MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = 0,
            dwFlags = flag,
            time = 0,
            dwExtraInfo = InjectionTag,
        };
        return SendInput(1, inputs, INPUT.Size) == 1;
    }
}
