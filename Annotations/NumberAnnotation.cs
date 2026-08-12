using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeCapture.Core;

namespace WeCapture.Annotations;

/// <summary>
/// 序号标注：在点击处放置递增编号徽章（圆底白字；多位数自动变为胶囊形）。
/// 徽章以点击位置为中心。
/// </summary>
public sealed class NumberAnnotation : Annotation
{
    public PointI Center { get; init; }
    public required int Index { get; init; }

    private const double BadgeHeightPx = 28;
    private const double FontSizePx = 17;
    private const double HPadPx = 8; // 多位数时的左右内边距

    private static readonly Typeface BoldTypeface = new(
        new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private FormattedText? _formatted;

    private FormattedText GetFormatted(in RenderEnv env)
    {
        if (_formatted == null)
        {
            _formatted = new FormattedText(
                Index.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                BoldTypeface,
                FontSizePx,
                Brushes.White,
                env.PixelsPerDip);
        }
        return _formatted;
    }

    private static double MeasureTextWidth(int index)
    {
        var ft = new FormattedText(index.ToString(), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, BoldTypeface, FontSizePx,
            Brushes.White, 1.0);
        return ft.Width;
    }

    // 字形 advance 宽度的左右 bearing 不等，直接按 Width 居中会偏左；
    // 栅格化一次扫描 alpha 求出真实 ink 中心（相对绘制原点），按文本缓存。
    private static readonly Dictionary<string, Point> InkCenterCache = new();

    private static Point InkCenter(FormattedText ft)
    {
        string key = $"{ft.Text}:{ft.Width:0.##}";
        if (InkCenterCache.TryGetValue(key, out var cached)) return cached;

        int w = (int)Math.Ceiling(ft.Width) + 4;
        int h = (int)Math.Ceiling(ft.Height) + 4;
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawText(ft, new Point(2, 2));
        rtb.Render(dv);

        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] <= 16) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        var center = minX == int.MaxValue
            ? new Point(ft.Width / 2, ft.Height / 2)
            : new Point((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        InkCenterCache[key] = center;
        return center;
    }

    private System.Windows.Rect BadgeRect(double textWidthPx)
    {
        double w = Math.Max(BadgeHeightPx, textWidthPx + HPadPx * 2);
        return new System.Windows.Rect(Center.X - w / 2, Center.Y - BadgeHeightPx / 2, w, BadgeHeightPx);
    }

    public override RectI BoundsPx
    {
        get
        {
            double tw = _formatted?.Width ?? MeasureTextWidth(Index);
            var r = BadgeRect(tw);
            return RectI.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top),
                (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
        }
    }

    public override void Render(DrawingContext dc, in RenderEnv env)
    {
        var ft = GetFormatted(in env);
        var r = BadgeRect(ft.Width);
        double radius = r.Height / 2; // 单位数=正圆，多位数=胶囊
        var bg = new RectangleGeometry(r, radius, radius);
        bg.Freeze();
        dc.DrawGeometry(BrushOf(Color), null, bg);

        // 按真实 ink 中心对齐（advance bearing 不对称，按宽度居中会偏左）
        var ink = InkCenter(ft);
        dc.DrawText(ft, new Point(Center.X - ink.X, Center.Y - ink.Y));
    }
}
