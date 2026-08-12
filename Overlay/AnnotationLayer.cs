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
