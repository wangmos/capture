using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeCapture.Annotations;
using WeCapture.Capture;
using WeCapture.Core;

namespace WeCapture.Export;

/// <summary>按物理像素渲染选区 + 标注，输出精确尺寸的位图。</summary>
public static class ImageExporter
{
    public static BitmapSource Render(MonitorSet monitors, RectI sel,
        IReadOnlyList<Annotation> annotations, BitmapSource? mosaic)
    {
        if (sel.IsEmpty)
            throw new ArgumentException("选区为空", nameof(sel));

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 1) 原始屏幕切片（DrawImage 按目标矩形缩放，位图自身 DPI 无关）
            foreach (var mon in monitors)
            {
                if (!mon.BoundsPx.IntersectsWith(sel)) continue;
                dc.DrawImage(mon.Image, new System.Windows.Rect(
                    mon.BoundsPx.X - sel.X, mon.BoundsPx.Y - sel.Y,
                    mon.BoundsPx.W, mon.BoundsPx.H));
            }

            // 2) 标注（裁剪到选区，平移到选区原点坐标系由标注自身使用全局坐标 + 偏移）
            dc.PushClip(new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(0, 0, sel.W, sel.H)));
            dc.PushTransform(new TranslateTransform(-sel.X, -sel.Y));

            var env = new RenderEnv(sel, mosaic, 1.0);
            foreach (var a in annotations)
                a.Render(dc, in env);

            dc.Pop();
            dc.Pop();
        }

        // 96dpi：1 绘制单位 = 1 物理像素，输出尺寸严格等于选区尺寸
        var rtb = new RenderTargetBitmap(sel.W, sel.H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
