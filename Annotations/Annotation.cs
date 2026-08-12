using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeCapture.Core;

namespace WeCapture.Annotations;

/// <summary>渲染上下文：选区（全局 px）、马赛克源图、文字 PixelsPerDip。</summary>
public readonly record struct RenderEnv(RectI Selection, BitmapSource? Mosaic, double PixelsPerDip);

/// <summary>标注基类。坐标一律为虚拟屏全局物理像素；裁剪/平移由渲染方负责。</summary>
public abstract class Annotation
{
    public Color Color { get; init; } = Colors.Red;

    /// <summary>线宽（物理像素）。</summary>
    public double ThicknessPx { get; init; } = 5;

    public abstract RectI BoundsPx { get; }

    public abstract void Render(DrawingContext dc, in RenderEnv env);

    protected static Brush BrushOf(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected static Pen PenOf(Color c, double thicknessPx)
    {
        var p = new Pen(BrushOf(c), thicknessPx)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        p.Freeze();
        return p;
    }

    protected static RectI RectFromPoints(PointI a, PointI b) => RectI.Normalize(a, b);
}
