using System.Windows.Controls;
using System.Windows.Input;
using WeCapture.Core;

namespace WeCapture.Settings;

/// <summary>热键捕获输入框：聚焦后按下组合键即记录，Esc 取消。</summary>
public sealed class HotkeyBox : TextBox
{
    private bool _capturing;

    public HotkeyDef Value { get; private set; } = HotkeyDef.Default.Clone();

    public bool IsCapturing => _capturing;

    public HotkeyBox()
    {
        IsReadOnly = true;
        Text = Value.ToString();
        GotKeyboardFocus += (_, _) => BeginCapture();
        LostKeyboardFocus += (_, _) => EndCapture();
    }

    public void SetValue(HotkeyDef def)
    {
        Value = def.Clone();
        if (!_capturing) Text = Value.ToString();
    }

    private void BeginCapture()
    {
        _capturing = true;
        Text = "请按下新热键（Esc 取消）";
    }

    private void EndCapture()
    {
        _capturing = false;
        Text = Value.ToString();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                  or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // 等真正的键

        if (key == Key.Escape)
        {
            EndCapture();
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return; // 必须带修饰键

        Value = new HotkeyDef
        {
            Ctrl = (mods & ModifierKeys.Control) != 0,
            Alt = (mods & ModifierKeys.Alt) != 0,
            Shift = (mods & ModifierKeys.Shift) != 0,
            Win = (mods & ModifierKeys.Windows) != 0,
            Key = key,
        };
        EndCapture();
    }
}
