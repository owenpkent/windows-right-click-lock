using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMouseMods.Core;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hold duration in milliseconds before ClickLock engages.</summary>
    public int ClickLockHoldMs { get; set; } = 500;

    public bool StartMinimized { get; set; } = false;
    public bool AutoStartWithWindows { get; set; } = false;

    /// <summary>Re-open the debug window automatically on launch if it was open last time.</summary>
    public bool ShowDebugOnStartup { get; set; } = false;

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
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                loaded.ClampInPlace();
                return loaded;
            }
        }
        catch
        {
            // Corrupt file — fall through to defaults. (Possible recovery: restore from .bak.)
        }
        return new AppSettings();
    }

    public void Save()
    {
        ClampInPlace();
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);

        // Atomic write: serialize to a temp file, then move-with-overwrite so a crash
        // mid-write can never leave us with a half-written settings.json.
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    private void ClampInPlace()
    {
        if (ClickLockHoldMs < 100) ClickLockHoldMs = 100;
        else if (ClickLockHoldMs > 3000) ClickLockHoldMs = 3000;
    }
}
