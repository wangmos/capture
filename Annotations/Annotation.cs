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

    // 渲染每帧都会调用这两个方法，而拖拽时每次鼠标移动就是一帧：
    // 不缓存的话每帧、每个标注都要新建并冻结一次 Brush/Pen，纯属垃圾。
    // 标注的 Color/ThicknessPx 都是 init 的，缓存天然安全。
    private static readonly Dictionary<uint, Brush> BrushCache = new();
    private static readonly Dictionary<(uint, double), Pen> PenCache = new();

    protected static Brush BrushOf(Color c)
    {
        uint key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (BrushCache.TryGetValue(key, out var cached)) return cached;

        var b = new SolidColorBrush(c);
        b.Freeze();
        BrushCache[key] = b;
        return b;
    }

    protected static Pen PenOf(Color c, double thicknessPx)
    {
        uint colorKey = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (PenCache.TryGetValue((colorKey, thicknessPx), out var cached)) return cached;

        var p = new Pen(BrushOf(c), thicknessPx)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        p.Freeze();
        PenCache[(colorKey, thicknessPx)] = p;
        return p;
    }

    protected static RectI RectFromPoints(PointI a, PointI b) => RectI.Normalize(a, b);
}
