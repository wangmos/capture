using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WeCapture.Core;

namespace WeCapture.Annotations;

public sealed class TextAnnotation : Annotation
{
    public PointI Position { get; init; }
    public required string Text { get; init; }

    /// <summary>字号（物理像素）。</summary>
    public double FontSizePx { get; init; } = 24;

    private FormattedText? _formatted;

    private FormattedText GetFormatted(in RenderEnv env)
    {
        if (_formatted == null)
        {
            _formatted = new FormattedText(
                Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei"),
                FontSizePx,
                BrushOf(Color),
                env.PixelsPerDip);
        }
        return _formatted;
    }

    public override RectI BoundsPx
    {
        get
        {
            if (_formatted == null)
            {
                // 未渲染过时用默认 DPI 估算
                var ft = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei"), FontSizePx, Brushes.Black, 1.0);
                return new RectI(Position.X, Position.Y, (int)Math.Ceiling(ft.Width), (int)Math.Ceiling(ft.Height));
            }
            return new RectI(Position.X, Position.Y,
                (int)Math.Ceiling(_formatted.Width), (int)Math.Ceiling(_formatted.Height));
        }
    }

    public override void Render(DrawingContext dc, in RenderEnv env)
    {
        if (string.IsNullOrEmpty(Text)) return;
        dc.DrawText(GetFormatted(in env), new Point(Position.X, Position.Y));
    }
}
