using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMouseMods.Core;

public enum LockMode
{
    HotkeyToggle,
    ClickLock,
}

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public LockMode Mode { get; set; } = LockMode.HotkeyToggle;

    /// <summary>Virtual-key code for the hotkey toggle. Default F8 (0x77).</summary>
    public int HotkeyVirtualKey { get; set; } = 0x77;

    /// <summary>Hold duration in milliseconds before ClickLock engages.</summary>
    public int ClickLockHoldMs { get; set; } = 500;

    public bool StartMinimized { get; set; } = true;
    public bool AutoStartWithWindows { get; set; } = false;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsMouseMods");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt file — fall through to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
