using Microsoft.Win32;

namespace WindowsMouseMods.Core;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsMouseMods";

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
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
