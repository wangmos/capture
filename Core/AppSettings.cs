using System.IO;
using System.Text.Json;

namespace WeCapture.Core;

/// <summary>应用设置（JSON 持久化于 %AppData%\WeCapture\settings.json）。</summary>
public sealed class AppSettings
{
    public HotkeyDef Hotkey { get; set; } = HotkeyDef.Default;
    public bool AutoStart { get; set; }
    public string? LastSaveDir { get; set; }

    private static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeCapture");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded != null)
                {
                    if (loaded.Hotkey == null || !loaded.Hotkey.IsValid)
                        loaded.Hotkey = HotkeyDef.Default;
                    return loaded;
                }
            }
        }
        catch
        {
            // 设置损坏时回退默认
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不影响主流程
        }
    }
}
