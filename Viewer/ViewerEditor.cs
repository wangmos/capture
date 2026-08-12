using System.Windows.Media;
using WeCapture.Annotations;
using WeCapture.Core;
using WeCapture.Session;

namespace WeCapture.Viewer;

/// <summary>
/// 图片查看窗里的标注编辑状态。坐标一律是**图片像素**，与缩放和滚动位置无关——
/// 视图负责把鼠标位置换算成图片坐标再交给这里，标注层再按当前缩放画回去。
/// </summary>
public sealed class ViewerEditor
{
    private const int MinSize = 3;

    private PointI _start;
    private List<PointI>? _stroke;

    public List<Annotation> Annotations { get; } = new();
    public Tool ActiveTool { get; private set; } = Tool.None;
    public bool IsDrawing { get; private set; }

    public Color DrawColor { get; set; } = Color.FromRgb(0xFF, 0x3B, 0x30);
    public double ThicknessPx { get; set; } = 5;
    public double FontSizePx { get; set; } = 24;

    public bool CanUndo => Annotations.Count > 0;

    /// <summary>标注集合或工具发生变化，需要重绘/刷新按钮。</summary>
    public event Action? Changed;

    /// <summary>请求在图片坐标处打开文字输入框。</summary>
    public event Action<PointI>? TextRequested;

    public void SetTool(Tool tool)
    {
        ActiveTool = ActiveTool == tool ? Tool.None : tool;
        TraceLog.Log($"Viewer SetTool {tool} active={ActiveTool}");
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (Annotations.Count == 0) return;
        Annotations.RemoveAt(Annotations.Count - 1);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Annotations.Count == 0) return;
        Annotations.Clear();
        Changed?.Invoke();
    }

    // ================= 鼠标（图片坐标） =================

    /// <returns>是否消费了这次按下（false 表示当前该走拖动浏览）。</returns>
    public bool OnDown(PointI p)
    {
        if (ActiveTool == Tool.None) return false;

        if (ActiveTool == Tool.Text)
        {
            TextRequested?.Invoke(p);
            return true;
        }

        if (ActiveTool == Tool.Number)
        {
            int idx = Annotations.Count(a => a is NumberAnnotation) + 1;
            Annotations.Add(new NumberAnnotation { Center = p, Index = idx, Color = DrawColor });
            TraceLog.Log($"Viewer number placed idx={idx} at {p}");
            Changed?.Invoke();
            return true;
        }

        _start = p;
        _stroke = new List<PointI> { p };
        IsDrawing = true;
        return true;
    }

    public void OnMove(PointI p)
    {
        if (!IsDrawing || _stroke == null) return;
        var last = _stroke[^1];
        if (Math.Abs(p.X - last.X) >= 1 || Math.Abs(p.Y - last.Y) >= 1)
            _stroke.Add(p);
        Changed?.Invoke();
    }

    public void OnUp(PointI p)
    {
        if (!IsDrawing) return;
        IsDrawing = false;

        var pts = _stroke;
        _stroke = null;
        if (pts == null) return;

        Annotation? ann = ActiveTool switch
        {
            Tool.Rectangle when Big(_start, p) => new RectAnnotation { P1 = _start, P2 = p, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Ellipse when Big(_start, p) => new EllipseAnnotation { P1 = _start, P2 = p, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Arrow when Big(_start, p) => new ArrowAnnotation { From = _start, To = p, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Pen when pts.Count >= 2 => new FreehandAnnotation { Points = pts, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Mosaic when Big(_start, p) => new MosaicAnnotation { P1 = _start, P2 = p },
            _ => null,
        };

        if (ann != null) Annotations.Add(ann);
        Changed?.Invoke();
    }

    /// <summary>拖拽过程中的临时标注（用于实时预览）。</summary>
    public Annotation? BuildPreview(PointI p)
    {
        if (!IsDrawing || _stroke == null) return null;
        return ActiveTool switch
        {
            Tool.Rectangle => new RectAnnotation { P1 = _start, P2 = p, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Ellipse => new EllipseAnnotation { P1 = _start, P2 = p, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Arrow => new ArrowAnnotation { From = _start, To = p, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Pen => new FreehandAnnotation { Points = _stroke, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Mosaic => new MosaicAnnotation { P1 = _start, P2 = p },
            _ => null,
        };
    }

    public void AddText(PointI at, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Annotations.Add(new TextAnnotation
        {
            Position = at,
            Text = text.TrimEnd('\r', '\n'),
            FontSizePx = FontSizePx,
            Color = DrawColor,
        });
        Changed?.Invoke();
    }

    private static bool Big(PointI a, PointI b) =>
        Math.Abs(a.X - b.X) >= MinSize || Math.Abs(a.Y - b.Y) >= MinSize;
}
