using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WeCapture.Core;

namespace WeCapture.Settings;

/// <summary>
/// 截图界面快捷键的捕获框。与全局热键的 <see cref="HotkeyBox"/> 不同，
/// 这里允许无修饰键的单字母（工具切换就是单字母），Delete/Backspace 清除绑定。
/// </summary>
public sealed class ShortcutBox : TextBox
{
    private bool _capturing;

    public ShortcutDef Value { get; private set; } = ShortcutDef.None;

    /// <summary>用户改动了绑定（设置窗据此做冲突提示）。</summary>
    public event Action<ShortcutBox>? Changed;

    public ShortcutBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        TextAlignment = TextAlignment.Center;
        GotKeyboardFocus += (_, _) => BeginCapture();
        LostKeyboardFocus += (_, _) => EndCapture();
        // 输入法会把字母键变成 ImeProcessed，这里同样要关掉
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    public void SetValue(ShortcutDef def)
    {
        Value = def;
        if (!_capturing) Render();
    }

    private void Render() => Text = Value.IsNone ? "未设置" : Value.ToString();

    private void BeginCapture()
    {
        _capturing = true;
        Text = "按下新快捷键…";
    }

    private void EndCapture()
    {
        _capturing = false;
        Render();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        if (!_capturing) return;

        var key = e.Key switch
        {
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.System => e.SystemKey,
            _ => e.Key,
        };

        // 修饰键本身不作为主键，等真正的键
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        // Esc 一律解释为"取消录制"，不作为可绑定的键（默认的"退出"仍然是 Esc）
        if (key == Key.Escape)
        {
            EndCapture();
            MoveFocusAway();
            return;
        }

        if (key is Key.Delete or Key.Back)
        {
            Value = ShortcutDef.None;
            EndCapture();
            Changed?.Invoke(this);
            MoveFocusAway();
            return;
        }

        if (key == Key.Tab) return;   // 留给焦点切换

        var mods = Keyboard.Modifiers;
        Value = new ShortcutDef(key,
            (mods & ModifierKeys.Control) != 0,
            (mods & ModifierKeys.Alt) != 0,
            (mods & ModifierKeys.Shift) != 0);

        EndCapture();
        Changed?.Invoke(this);
        MoveFocusAway();
    }

    /// <summary>捕获完成后交出焦点，避免继续吞按键。</summary>
    private void MoveFocusAway() =>
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
}
