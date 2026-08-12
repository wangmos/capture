using System.Windows;
using System.Windows.Media;
using WeCapture.Core;

namespace WeCapture.Annotations;

public sealed class ArrowAnnotation : Annotation
{
    public PointI From { get; init; }
    public PointI To { get; init; }

    private const double HeadAngleDeg = 25;

    public override RectI BoundsPx
    {
        get
        {
            int l = Math.Min(From.X, To.X), t = Math.Min(From.Y, To.Y);
            int r = Math.Max(From.X, To.X), b = Math.Max(From.Y, To.Y);
            int pad = (int)HeadLength + 4;
            return RectI.FromLTRB(l - pad, t - pad, r + pad, b + pad);
        }
    }

    private double HeadLength => Math.Max(12, ThicknessPx * 3.5);

    public override void Render(DrawingContext dc, in RenderEnv env)
    {
        double dx = To.X - From.X;
        double dy = To.Y - From.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 2) return;

        var pen = PenOf(Color, ThicknessPx);
        double angle = Math.Atan2(dy, dx);
        double head = Math.Min(HeadLength, len);
        double a = HeadAngleDeg * Math.PI / 180;

        // 箭杆（画到箭头根部，避免箭头顶端穿出）
        var shaftEnd = new Point(To.X - Math.Cos(angle) * head * 0.6, To.Y - Math.Sin(angle) * head * 0.6);
        dc.DrawLine(pen, new Point(From.X, From.Y), shaftEnd);

        // 箭头三角
        var p1 = new Point(To.X - Math.Cos(angle - a) * head, To.Y - Math.Sin(angle - a) * head);
        var p2 = new Point(To.X - Math.Cos(angle + a) * head, To.Y - Math.Sin(angle + a) * head);
        var fig = new PathFigure(new Point(To.X, To.Y),
            new[] { new PolyLineSegment(new[] { p1, p2 }, true) }, true);
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        dc.DrawGeometry(BrushOf(Color), null, geo);
    }
}
