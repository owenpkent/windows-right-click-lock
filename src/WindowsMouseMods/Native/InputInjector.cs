using static WindowsMouseMods.Native.NativeMethods;

namespace WindowsMouseMods.Native;

internal static class InputInjector
{
    public static void RightDown() => SendMouseFlag(MOUSEEVENTF_RIGHTDOWN);
    public static void RightUp() => SendMouseFlag(MOUSEEVENTF_RIGHTUP);

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
