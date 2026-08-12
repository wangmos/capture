using System.Windows.Media;
using WeCapture.Core;

namespace WeCapture.Annotations;

/// <summary>自由画笔（也作为马赛克笔触的几何基础）。</summary>
public class FreehandAnnotation : Annotation
{
    public required IReadOnlyList<PointI> Points { get; init; }

    public override RectI BoundsPx
    {
        get
        {
            if (Points.Count == 0) return default;
            int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
            foreach (var p in Points)
            {
                l = Math.Min(l, p.X); t = Math.Min(t, p.Y);
                r = Math.Max(r, p.X); b = Math.Max(b, p.Y);
            }
            int pad = (int)Math.Ceiling(ThicknessPx / 2) + 1;
            return RectI.FromLTRB(l - pad, t - pad, r + pad, b + pad);
        }
    }

    private StreamGeometry? _cached;
    private int _cachedCount = -1;

    public override void Render(DrawingContext dc, in RenderEnv env)
    {
        // 已提交的笔迹点集不再变化，几何缓存下来即可；拖拽中的预览笔迹点数一直在增长，
        // 用点数当版本号，增长时自然重建。否则每帧都要按全部点重建一次几何。
        if (_cached == null || _cachedCount != Points.Count)
        {
            _cached = BuildGeometry(Points, ThicknessPx);
            _cachedCount = Points.Count;
        }

        if (_cached != null)
            dc.DrawGeometry(null, PenOf(Color, ThicknessPx), _cached);
    }

    /// <summary>圆头折线几何（全局 px 坐标）。</summary>
    internal static StreamGeometry? BuildGeometry(IReadOnlyList<PointI> points, double thickness)
    {
        if (points.Count == 0) return null;

        var sg = new StreamGeometry();
        using (var sgc = sg.Open())
        {
            sgc.BeginFigure(new System.Windows.Point(points[0].X, points[0].Y), false, false);
            if (points.Count == 1)
            {
                // 单点：画一小段使笔迹可见
                sgc.LineTo(new System.Windows.Point(points[0].X + 0.1, points[0].Y + 0.1), true, false);
            }
            else
            {
                for (int i = 1; i < points.Count; i++)
                    sgc.LineTo(new System.Windows.Point(points[i].X, points[i].Y), true, false);
            }
        }
        sg.Freeze();
        return sg;
    }
}
