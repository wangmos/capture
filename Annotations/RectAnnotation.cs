using System.Windows;
using System.Windows.Media;
using WeCapture.Core;

namespace WeCapture.Annotations;

public sealed class RectAnnotation : Annotation
{
    public PointI P1 { get; init; }
    public PointI P2 { get; init; }

    public override RectI BoundsPx
    {
        get
        {
            var r = RectI.Normalize(P1, P2);
            int pad = (int)Math.Ceiling(ThicknessPx / 2);
            return new RectI(r.X - pad, r.Y - pad, r.W + pad * 2, r.H + pad * 2);
        }
    }

    public override void Render(DrawingContext dc, in RenderEnv env)
    {
        var r = RectI.Normalize(P1, P2);
        if (r.IsEmpty) return;
        dc.DrawRectangle(null, PenOf(Color, ThicknessPx),
            new Rect(r.X + ThicknessPx / 2, r.Y + ThicknessPx / 2,
                     Math.Max(0, r.W - ThicknessPx), Math.Max(0, r.H - ThicknessPx)));
    }
}
