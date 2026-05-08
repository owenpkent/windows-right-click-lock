using Microsoft.Win32;

namespace WindowsRightClickLock.Core;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsRightClickLock";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            // Reject paths that cannot be safely round-tripped through the Run-key shell parser.
            // A literal '"' or control character would let an attacker who controls the binary
            // location confuse the parser into resolving a different executable on next logon.
            // See docs/security-review.md (M3).
            foreach (var ch in exe)
            {
                if (ch == '"' || char.IsControl(ch))
                    throw new InvalidOperationException(
                        $"Executable path contains characters that cannot be safely written to the Run registry key: {exe}");
            }
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
