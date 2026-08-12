using System.Windows.Input;
using WeCapture.Native;

namespace WeCapture.Core;

/// <summary>全局热键定义（可序列化）。</summary>
public sealed class HotkeyDef
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public Key Key { get; set; } = Key.A;

    public static HotkeyDef Default => new();

    public uint ModifiersWin32()
    {
        uint m = NativeMethods.MOD_NOREPEAT;
        if (Ctrl) m |= NativeMethods.MOD_CONTROL;
        if (Alt) m |= NativeMethods.MOD_ALT;
        if (Shift) m |= NativeMethods.MOD_SHIFT;
        if (Win) m |= NativeMethods.MOD_WIN;
        return m;
    }

    public uint VirtualKey() => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>无修饰键的热键无效。</summary>
    public bool IsValid => Ctrl || Alt || Shift || Win;

    public HotkeyDef Clone() => new() { Ctrl = Ctrl, Alt = Alt, Shift = Shift, Win = Win, Key = Key };

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyToString(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyToString(Key key) => key switch
    {
        Key.Oem3 => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemBackslash => "\\",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.Space => "空格",
        Key.Decimal => ".",
        _ => key.ToString(),
    };
}
