using System.Windows.Input;

namespace WeCapture.Core;

/// <summary>截图界面里可绑定快捷键的动作。</summary>
public enum ShortcutAction
{
    ToolRectangle,
    ToolEllipse,
    ToolArrow,
    ToolPen,
    ToolText,
    ToolMosaic,
    ToolNumber,
    ToolTextSelect,
    Undo,
    SelectAllText,
    Ocr,
    LongShot,
    Pin,
    Save,
    Copy,
    Confirm,
    Exit,
}

/// <summary>动作 → 快捷键的映射（可由用户自定义，持久化在设置里）。</summary>
public sealed class ShortcutMap
{
    private readonly Dictionary<ShortcutAction, ShortcutDef> _map;

    private ShortcutMap(Dictionary<ShortcutAction, ShortcutDef> map) => _map = map;

    /// <summary>界面上显示的动作名（按此顺序列出）。</summary>
    public static readonly (ShortcutAction Action, string Name)[] Catalog =
    {
        (ShortcutAction.ToolRectangle, "矩形"),
        (ShortcutAction.ToolEllipse, "椭圆"),
        (ShortcutAction.ToolArrow, "箭头"),
        (ShortcutAction.ToolPen, "画笔"),
        (ShortcutAction.ToolText, "文字"),
        (ShortcutAction.ToolMosaic, "马赛克"),
        (ShortcutAction.ToolNumber, "标号"),
        (ShortcutAction.ToolTextSelect, "取字"),
        (ShortcutAction.Undo, "撤销"),
        (ShortcutAction.SelectAllText, "全选文字"),
        (ShortcutAction.Ocr, "识别全部文字"),
        (ShortcutAction.LongShot, "长截图"),
        (ShortcutAction.Pin, "钉住"),
        (ShortcutAction.Save, "保存"),
        (ShortcutAction.Copy, "复制"),
        (ShortcutAction.Confirm, "确认并复制"),
        (ShortcutAction.Exit, "取消 / 退出"),
    };

    public static ShortcutMap CreateDefault() => new(new()
    {
        [ShortcutAction.ToolRectangle] = new(Key.R),
        [ShortcutAction.ToolEllipse] = new(Key.O),
        [ShortcutAction.ToolArrow] = new(Key.A),
        [ShortcutAction.ToolPen] = new(Key.P),
        [ShortcutAction.ToolText] = new(Key.T),
        [ShortcutAction.ToolMosaic] = new(Key.M),
        [ShortcutAction.ToolNumber] = new(Key.N),
        [ShortcutAction.ToolTextSelect] = new(Key.I),
        [ShortcutAction.Undo] = new(Key.Z, Ctrl: true),
        [ShortcutAction.SelectAllText] = new(Key.A, Ctrl: true),
        [ShortcutAction.Ocr] = new(Key.E, Ctrl: true),
        [ShortcutAction.LongShot] = new(Key.L, Ctrl: true),
        [ShortcutAction.Pin] = new(Key.P, Ctrl: true),
        [ShortcutAction.Save] = new(Key.S, Ctrl: true),
        [ShortcutAction.Copy] = new(Key.C, Ctrl: true),
        [ShortcutAction.Confirm] = new(Key.Enter),
        [ShortcutAction.Exit] = new(Key.Escape),
    });

    public ShortcutDef Get(ShortcutAction action) =>
        _map.TryGetValue(action, out var d) ? d : ShortcutDef.None;

    /// <summary>绑定快捷键；同一组合已被别的动作占用时，先解除对方的绑定。</summary>
    public void Set(ShortcutAction action, ShortcutDef def)
    {
        if (!def.IsNone)
        {
            foreach (var other in _map.Keys.ToList())
                if (other != action && _map[other] == def)
                    _map[other] = ShortcutDef.None;
        }
        _map[action] = def;
    }

    /// <summary>按下的键对应哪个动作（无匹配返回 null）。</summary>
    public ShortcutAction? Resolve(Key key, ModifierKeys mods)
    {
        foreach (var (action, def) in _map)
            if (def.Matches(key, mods))
                return action;
        return null;
    }

    public ShortcutMap Clone() => new(new Dictionary<ShortcutAction, ShortcutDef>(_map));

    public Dictionary<string, string> ToDictionary()
    {
        var d = new Dictionary<string, string>();
        foreach (var (action, def) in _map)
            d[action.ToString()] = def.ToString();
        return d;
    }

    /// <summary>从设置文件恢复；缺失或非法的项回落到默认值。</summary>
    public static ShortcutMap FromDictionary(Dictionary<string, string>? stored)
    {
        var map = CreateDefault();
        if (stored == null) return map;

        foreach (var (name, text) in stored)
        {
            if (!Enum.TryParse<ShortcutAction>(name, out var action)) continue;
            // 空串是用户主动清除的绑定，要保留为"无"
            map._map[action] = ShortcutDef.Parse(text);
        }
        return map;
    }
}
