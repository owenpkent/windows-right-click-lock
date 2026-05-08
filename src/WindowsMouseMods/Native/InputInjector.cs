using static WindowsMouseMods.Native.NativeMethods;

namespace WindowsMouseMods.Native;

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

    public static void RightDown()
    {
        SendMouseFlag(MOUSEEVENTF_RIGHTDOWN);
        MarkHeld(true);
    }

    public static void RightUp()
    {
        SendMouseFlag(MOUSEEVENTF_RIGHTUP);
        MarkHeld(false);
    }

    /// <summary>Best-effort release for use from finalizer / unhandled-exception paths.</summary>
    public static void EmergencyRelease()
    {
        if (!IsHeld) return;
        try { SendMouseFlag(MOUSEEVENTF_RIGHTUP); }
        catch { /* swallow — we're already on a failure path */ }
        finally { MarkHeld(false); }
    }

    private static void SendMouseFlag(uint flag)
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
        _ = SendInput(1, inputs, INPUT.Size);
    }
}
