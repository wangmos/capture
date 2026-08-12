using System.Windows.Input;

namespace WeCapture.Core;

/// <summary>
/// 截图界面内的快捷键（与全局热键 <see cref="HotkeyDef"/> 不同，这里允许无修饰键的单字母）。
/// 序列化成 "Ctrl+S" / "R" 这类文本存进设置文件。
/// </summary>
public readonly record struct ShortcutDef(Key Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public static ShortcutDef None => new(Key.None);

    public bool IsNone => Key == Key.None;

    public bool Matches(Key key, ModifierKeys mods) =>
        !IsNone && key == Key &&
        Ctrl == ((mods & ModifierKeys.Control) != 0) &&
        Alt == ((mods & ModifierKeys.Alt) != 0) &&
        Shift == ((mods & ModifierKeys.Shift) != 0);

    public override string ToString()
    {
        if (IsNone) return "";
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(HotkeyDef.KeyToString(Key));
        return string.Join("+", parts);
    }

    public static ShortcutDef Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;

        bool ctrl = false, alt = false, shift = false;
        Key key = Key.None;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default:
                    key = ParseKey(part);
                    break;
            }
        }

        return key == Key.None ? None : new ShortcutDef(key, ctrl, alt, shift);
    }

    private static Key ParseKey(string s)
    {
        // 与 HotkeyDef.KeyToString 的符号显示保持一致
        switch (s)
        {
            case "`": return Key.Oem3;
            case "-": return Key.OemMinus;
            case "=": return Key.OemPlus;
            case "[": return Key.OemOpenBrackets;
            case "]": return Key.OemCloseBrackets;
            case "\\": return Key.OemBackslash;
            case ";": return Key.OemSemicolon;
            case "'": return Key.OemQuotes;
            case ",": return Key.OemComma;
            case ".": return Key.OemPeriod;
            case "/": return Key.OemQuestion;
            case "空格": return Key.Space;
        }
        return Enum.TryParse<Key>(s, ignoreCase: true, out var k) ? k : Key.None;
    }
}
