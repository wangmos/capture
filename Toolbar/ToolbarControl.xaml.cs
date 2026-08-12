using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using WeCapture.Session;

namespace WeCapture.Toolbar;

/// <summary>浮动工具条：7 标注工具 + 撤销/OCR/钉住/保存/复制/退出 + 颜色线宽子面板。</summary>
public partial class ToolbarControl : UserControl
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xFF, 0x3B, 0x30), // 红
        Color.FromRgb(0xFF, 0xCC, 0x00), // 黄
        Color.FromRgb(0x1E, 0x90, 0xFF), // 蓝
        Color.FromRgb(0x2B, 0xD1, 0x2B), // 绿
        Color.FromRgb(0x26, 0x26, 0x26), // 黑
    };
    private static readonly double[] Thicknesses = { 3, 5, 8 };
    private static readonly double[] FontSizes = { 20, 28, 36 };
    private static readonly double[] MosaicRadii = { 12, 20, 32 };

    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private readonly Button _undoButton;
    private SessionModel? _model;
    private string _lastPanelKey = "";

    public event Action<Tool>? ToolSelected;
    public event Action? UndoClicked;
    public event Action? OcrClicked;
    public event Action? PinClicked;
    public event Action? SaveClicked;
    public event Action? CopyClicked;
    public event Action? ExitClicked;
    public event Action<Color>? ColorSelected;
    public event Action<double>? ThicknessSelected;
    public event Action<double>? FontSizeSelected;
    public event Action<double>? MosaicRadiusSelected;

    public ToolbarControl()
    {
        InitializeComponent();
        BuildButtons();

        _undoButton = FindUndoButton();
    }

    private Button FindUndoButton() => _buttons[0];

    private readonly List<Button> _buttons = new();

    private void BuildButtons()
    {
        var tools = new (Tool Tool, string Tip, Func<UIElement> Icon)[]
        {
            (Tool.Rectangle, "矩形", () => ToolIcons.Create(Tool.Rectangle)),
            (Tool.Ellipse, "椭圆", () => ToolIcons.Create(Tool.Ellipse)),
            (Tool.Arrow, "箭头", () => ToolIcons.Create(Tool.Arrow)),
            (Tool.Pen, "画笔", () => ToolIcons.Create(Tool.Pen)),
            (Tool.Text, "文字", () => ToolIcons.TextGlyph("A", 14)),
            (Tool.Mosaic, "马赛克", () => ToolIcons.Create(Tool.Mosaic)),
            (Tool.Number, "标号（序号从 1 递增，区域外点击自动扩展选区）", () => ToolIcons.Number()),
        };

        foreach (var (tool, tip, icon) in tools)
        {
            var b = new ToggleButton
            {
                Style = (Style)FindResource("TbToggle"),
                Content = icon(),
                ToolTip = tip,
            };
            b.Click += (_, _) => ToolSelected?.Invoke(tool);
            _toolButtons[tool] = b;
            ButtonsRow.Children.Add(b);
        }

        AddSeparator();

        _buttons.Clear();
        var undo = MakeButton(ToolIcons.Undo(), "撤销", () => UndoClicked?.Invoke());
        ButtonsRow.Children.Add(undo);
        _buttons.Add(undo);

        // 取字是模式开关（非一次性动作），所以做成 ToggleButton 并登记到工具表
        var textSelect = new ToggleButton
        {
            Style = (Style)FindResource("TbToggle"),
            Content = ToolIcons.TextSelect(),
            ToolTip = "取字（在图上直接拖选文字，Ctrl+C 复制）",
        };
        textSelect.Click += (_, _) => ToolSelected?.Invoke(Tool.TextSelect);
        _toolButtons[Tool.TextSelect] = textSelect;
        ButtonsRow.Children.Add(textSelect);

        var ocr = MakeButton(ToolIcons.Ocr(), "识别图片中的文字", () => OcrClicked?.Invoke());
        ButtonsRow.Children.Add(ocr);
        _buttons.Add(ocr);

        AddSeparator();

        var pin = MakeButton(ToolIcons.Pin(), "钉住", () => PinClicked?.Invoke());
        ButtonsRow.Children.Add(pin);
        _buttons.Add(pin);

        var save = MakeButton(ToolIcons.Save(), "保存", () => SaveClicked?.Invoke());
        ButtonsRow.Children.Add(save);
        _buttons.Add(save);

        var copy = MakeButton(ToolIcons.Copy(), "复制", () => CopyClicked?.Invoke());
        ButtonsRow.Children.Add(copy);
        _buttons.Add(copy);

        AddSeparator();

        var exit = MakeButton(ToolIcons.Exit(), "退出截图", () => ExitClicked?.Invoke());
        ButtonsRow.Children.Add(exit);
        _buttons.Add(exit);
    }

    private Button MakeButton(UIElement icon, string tip, Action onClick)
    {
        var b = new Button
        {
            Style = (Style)FindResource("TbButton"),
            Content = icon,
            ToolTip = tip,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void AddSeparator()
    {
        ButtonsRow.Children.Add(new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(4, 5, 4, 5),
        });
    }

    /// <summary>按模型状态刷新（去重，避免拖拽时频繁重建）。</summary>
    public void RefreshFrom(SessionModel model)
    {
        _model = model;

        foreach (var (tool, btn) in _toolButtons)
            btn.IsChecked = model.ActiveTool == tool;

        _undoButton.IsEnabled = model.CanUndo;
        _undoButton.Opacity = model.CanUndo ? 1.0 : 0.4;

        string key = $"{model.ActiveTool}|{model.DrawColor}|{model.ThicknessPx}|{model.FontSizePx}|{model.MosaicRadiusPx}";
        if (key == _lastPanelKey) return;
        _lastPanelKey = key;

        BuildStylePanel(model);
    }

    private void BuildStylePanel(SessionModel model)
    {
        StylePanel.Children.Clear();

        // 取字没有样式可调，和"无工具"一样收起面板（也保证工具条高度不变）
        if (model.ActiveTool is Tool.None or Tool.TextSelect)
        {
            StylePanel.Visibility = Visibility.Collapsed;
            return;
        }

        StylePanel.Visibility = Visibility.Visible;

        bool needColor = model.ActiveTool is Tool.Rectangle or Tool.Ellipse or Tool.Arrow or Tool.Pen or Tool.Text or Tool.Number;
        if (needColor)
        {
            foreach (var c in Palette)
            {
                var dot = MakeDot(12, new SolidColorBrush(c), true);
                if (c == model.DrawColor)
                    MarkSelected(dot);
                var cc = c;
                dot.MouseLeftButtonUp += (_, _) => ColorSelected?.Invoke(cc);
                StylePanel.Children.Add(dot);
            }
            StylePanel.Children.Add(new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(5, 2, 5, 2),
            });
        }

        switch (model.ActiveTool)
        {
            case Tool.Rectangle or Tool.Ellipse or Tool.Arrow or Tool.Pen:
                foreach (var t in Thicknesses)
                {
                    var dot = MakeDot(12, Brushes.White, false);
                    dot.Child = new Ellipse
                    {
                        Width = t + 1, Height = t + 1,
                        Fill = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    if (Math.Abs(t - model.ThicknessPx) < 0.1)
                        MarkSelected(dot);
                    var tt = t;
                    dot.MouseLeftButtonUp += (_, _) => ThicknessSelected?.Invoke(tt);
                    StylePanel.Children.Add(dot);
                }
                break;

            case Tool.Text:
                foreach (var f in FontSizes)
                {
                    var holder = MakeDot(16, null, false);
                    holder.Child = ToolIcons.TextGlyph("A", 8 + (f - 20) / 2);
                    if (Math.Abs(f - model.FontSizePx) < 0.1)
                        MarkSelected(holder);
                    var ff = f;
                    holder.MouseLeftButtonUp += (_, _) => FontSizeSelected?.Invoke(ff);
                    StylePanel.Children.Add(holder);
                }
                break;

            case Tool.Mosaic:
                foreach (var r in MosaicRadii)
                {
                    double d = r / 2.5;
                    var holder = MakeDot(16, null, false);
                    holder.Child = new Ellipse
                    {
                        Width = d, Height = d,
                        Fill = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    if (Math.Abs(r - model.MosaicRadiusPx) < 0.1)
                        MarkSelected(holder);
                    var rr = r;
                    holder.MouseLeftButtonUp += (_, _) => MosaicRadiusSelected?.Invoke(rr);
                    StylePanel.Children.Add(holder);
                }
                break;
        }
    }

    /// <summary>
    /// 把每个按钮的屏幕矩形（物理像素）写进日志，仅在布局变化时输出一次。
    /// UI 测试脚本据此定位按钮，避免硬编码坐标——加删按钮会让其左侧所有按钮平移。
    /// </summary>
    public void LogButtonRects()
    {
        if (!IsLoaded || ActualWidth <= 0) return;

        var sb = new System.Text.StringBuilder("ToolbarRects");
        foreach (var (tool, btn) in _toolButtons)
            AppendRect(sb, tool.ToString(), btn);
        foreach (var (name, btn) in NamedButtons())
            AppendRect(sb, name, btn);

        string s = sb.ToString();
        if (s == _lastRectLog) return;
        _lastRectLog = s;
        Core.TraceLog.Log(s);
    }

    private string _lastRectLog = "";

    private IEnumerable<(string Name, FrameworkElement Button)> NamedButtons()
    {
        string[] names = { "undo", "ocr", "pin", "save", "copy", "exit" };
        for (int i = 0; i < _buttons.Count && i < names.Length; i++)
            yield return (names[i], _buttons[i]);
    }

    private static void AppendRect(System.Text.StringBuilder sb, string name, FrameworkElement el)
    {
        if (el.ActualWidth <= 0 || !el.IsVisible) return;
        try
        {
            var p = el.PointToScreen(new System.Windows.Point(el.ActualWidth / 2, el.ActualHeight / 2));
            sb.Append($" {name}={(int)Math.Round(p.X)},{(int)Math.Round(p.Y)}");
        }
        catch
        {
            // 窗口还没建好句柄时 PointToScreen 会抛，忽略即可，下一轮布局还会再记
        }
    }

    /// <summary>色板圆点容器。</summary>
    private static Border MakeDot(double size, Brush? fill, bool fillDot)
    {
        var border = new Border
        {
            Width = size + 6,
            Height = size + 6,
            CornerRadius = new CornerRadius((size + 6) / 2),
            Background = Brushes.Transparent,
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        if (fillDot && fill != null)
        {
            border.Child = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = fill,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        return border;
    }

    private static void MarkSelected(Border dot)
    {
        dot.BorderBrush = Brushes.White;
        dot.BorderThickness = new Thickness(1.5);
    }
}
