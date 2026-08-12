using System.IO;
using System.Text.Json;

namespace WeCapture.Core;

/// <summary>应用设置（JSON 持久化于 %AppData%\WeCapture\settings.json）。</summary>
public sealed class AppSettings
{
    public HotkeyDef Hotkey { get; set; } = HotkeyDef.Default;
    public bool AutoStart { get; set; }
    public string? LastSaveDir { get; set; }

    /// <summary>框选完成后自动进入取字模式（后台识别一次，可直接在图上选文字）。</summary>
    public bool AutoTextSelect { get; set; } = true;

    /// <summary>截图界面内的快捷键，动作名 → 组合键文本；缺失项用默认值。</summary>
    public Dictionary<string, string>? Shortcuts { get; set; }

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
        catch (Exception ex)
        {
            // 设置损坏时回退默认。必须留痕：否则一个坏字段会让用户悄无声息地丢掉全部设置
            TraceLog.Log($"AppSettings.Load failed, falling back to defaults: {ex.Message}");
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
