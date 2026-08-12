using System.Windows.Media;
using WeCapture.Core;

namespace WeCapture.Annotations;

/// <summary>马赛克块：一次拖拽圈出矩形区域即完成整块马赛克（同微信）。</summary>
public sealed class MosaicAnnotation : Annotation
{
    public required PointI P1 { get; init; }
    public required PointI P2 { get; init; }

    public override RectI BoundsPx => RectI.Normalize(P1, P2);

    public override void Render(DrawingContext dc, in RenderEnv env)
    {
        if (env.Mosaic == null) return;
        var r = RectI.Normalize(P1, P2);
        if (r.IsEmpty) return;

        var clip = new RectangleGeometry(new System.Windows.Rect(r.X, r.Y, r.W, r.H));
        clip.Freeze();
        dc.PushClip(clip);
        dc.DrawImage(env.Mosaic,
            new System.Windows.Rect(env.Selection.X, env.Selection.Y, env.Selection.W, env.Selection.H));
        dc.Pop();
    }
}
