using System.Windows;
using System.Windows.Media;
using WeCapture.Annotations;
using WeCapture.Capture;
using WeCapture.Core;
using WeCapture.Session;

namespace WeCapture.Overlay;

/// <summary>
/// 覆盖层自绘元素：遮罩挖洞 → 悬停框 → 选区+手柄 → 标注（裁剪到选区）。
/// 外层套 ScaleTransform(1/dpiScale)，内部一律按本屏局部物理像素作画。
/// </summary>
public sealed class AnnotationLayer : FrameworkElement
{
    private static readonly Brush MaskBrush = CreateFrozen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)));
    private static readonly Pen HoverPen = CreateFrozenPen(Color.FromRgb(0x1E, 0x90, 0xFF), 2);
    private static readonly Pen SelectionPen = CreateFrozenPen(Color.FromRgb(0x1E, 0x90, 0xFF), 2);
    private static readonly Brush HandleBrush = CreateFrozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)));

    private SessionModel? _model;
    private MonitorShot? _monitor;
    private CaptureSession? _session;

    public void Attach(CaptureSession session, MonitorShot monitor)
    {
        _session = session;
        _model = session.Model;
        _monitor = monitor;
        _model.Changed += OnModelChanged;
        RenderTransform = new ScaleTransform(1.0 / monitor.DpiScale, 1.0 / monitor.DpiScale);
    }

    public void Detach()
    {
        if (_model != null)
            _model.Changed -= OnModelChanged;
    }

    private void OnModelChanged()
    {
        if (Dispatcher.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.BeginInvoke(InvalidateVisual);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var m = _model;
        var mon = _monitor;
        if (m == null || mon == null) return;

        var vb = mon.BoundsPx;

        // ---------- 1. 遮罩：全屏 Exclude（选区/悬停框）----------
        Geometry mask = new RectangleGeometry(new Rect(0, 0, vb.W, vb.H));

        RectI? cutout = m.State switch
        {
            UIState.Idle => m.HoverRect,
            _ => m.Selection,
        };
        if (cutout is RectI c && c.IntersectsWith(vb) && !c.IsEmpty)
        {
            var hole = new RectangleGeometry(ToLocal(c, vb));
            mask = new CombinedGeometry(GeometryCombineMode.Exclude, mask, hole);
        }
        dc.DrawGeometry(MaskBrush, null, mask);

        // ---------- 2. 悬停高亮框（Idle） ----------
        if (m.State == UIState.Idle && m.HoverRect is RectI hv && hv.IntersectsWith(vb))
            dc.DrawRectangle(null, HoverPen, ToLocal(hv, vb));

        // ---------- 3. 选区边框 + 手柄 ----------
        if (m.Selection is RectI sel && !sel.IsEmpty && sel.IntersectsWith(vb))
        {
            var ls = ToLocal(sel, vb);
            dc.DrawRectangle(null, SelectionPen, ls);

            if (m.State != UIState.Selecting)
            {
                double half = SelectionHitTester.HandleSize / 2.0;
                foreach (var hp in SelectionHitTester.HandlePoints(sel))
                {
                    dc.DrawRectangle(HandleBrush, null,
                        new Rect(hp.X - vb.X - half, hp.Y - vb.Y - half,
                                 SelectionHitTester.HandleSize, SelectionHitTester.HandleSize));
                }
            }
        }

        // ---------- 4. 标注 + 拖拽预览（裁剪到选区∩本屏） ----------
        if (m.Selection is RectI sel2 && !sel2.IsEmpty &&
            (m.Annotations.Count > 0 || m.DragMode == DragMode.Draw))
        {
            var clipLocal = ToLocal(sel2.Intersect(vb), vb);
            dc.PushClip(new RectangleGeometry(clipLocal));
            dc.PushTransform(new System.Windows.Media.TranslateTransform(-vb.X, -vb.Y));

            var env = new RenderEnv(sel2, _session?.GetMosaicImage(), 1.0);
            foreach (var a in m.Annotations)
                a.Render(dc, in env);

            if (m.DragMode == DragMode.Draw)
            {
                var preview = m.BuildPreviewAnnotation(LastMouseGlobal);
                preview?.Render(dc, in env);
            }

            dc.Pop();
            dc.Pop();
        }

        // ---------- 5. 取字模式：文字层与选中高亮 ----------
        if (m.ActiveTool == Session.Tool.TextSelect && m.Selection is RectI sel3 && !sel3.IsEmpty)
            RenderTextLayer(dc, m, sel3, vb);
    }

    private static readonly Pen TextBoxPen = CreateFrozenPen(Color.FromArgb(0x66, 0x1E, 0x90, 0xFF), 1);
    private static readonly Brush TextHighlightBrush =
        CreateFrozen(new SolidColorBrush(Color.FromArgb(0x59, 0x1E, 0x90, 0xFF)));
    private static readonly Brush HintBackBrush =
        CreateFrozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x20, 0x20, 0x20)));

    private void RenderTextLayer(DrawingContext dc, SessionModel m, RectI sel, RectI vb)
    {
        var clipLocal = ToLocal(sel.Intersect(vb), vb);
        if (clipLocal.Width <= 0 || clipLocal.Height <= 0) return;

        dc.PushClip(new RectangleGeometry(clipLocal));
        dc.PushTransform(new TranslateTransform(-vb.X, -vb.Y));

        if (m.TextLayer is { IsEmpty: false } layer)
        {
            // 文字行框：淡蓝细线，提示"这里可以选"
            foreach (var box in layer.LineBoxes)
                if (box.IntersectsWith(vb))
                    dc.DrawRectangle(null, TextBoxPen, new Rect(box.X, box.Y, box.W, box.H));

            if (m.HasTextSelection)
                foreach (var r in layer.HighlightRects(m.TextSelectionStart, m.TextSelectionEnd))
                    dc.DrawRectangle(TextHighlightBrush, null, new Rect(r.X, r.Y, r.W, r.H));
        }

        // 加载中 / 无文字 / 识别失败（层为 null 且已结束加载）都要给个交代
        string? hint =
            m.TextLayerLoading ? "识别中…" :
            m.TextLayer is null or { IsEmpty: true } ? "未识别到文字" :
            null;

        if (hint != null)
            DrawHint(dc, hint, sel);

        dc.Pop();
        dc.Pop();
    }

    /// <summary>选区左上角的状态提示（深色药丸 + 白字）。</summary>
    private static void DrawHint(DrawingContext dc, string text, RectI sel)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 14,
            Brushes.White, 1.0);

        double padX = 10, padY = 6;
        var rect = new Rect(sel.X + 8, sel.Y + 8, ft.Width + padX * 2, ft.Height + padY * 2);
        var pill = new RectangleGeometry(rect, 4, 4);
        pill.Freeze();
        dc.DrawGeometry(HintBackBrush, null, pill);
        dc.DrawText(ft, new Point(rect.X + padX, rect.Y + padY));
    }

    /// <summary>最近一次鼠标全局坐标（预览笔画终点）。</summary>
    public PointI LastMouseGlobal { get; set; }

    private static Rect ToLocal(RectI r, RectI vb) =>
        new(r.X - vb.X, r.Y - vb.Y, r.W, r.H);

    private static Brush CreateFrozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen CreateFrozenPen(Color c, double thickness)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        var p = new Pen(brush, thickness);
        p.Freeze();
        return p;
    }
}
