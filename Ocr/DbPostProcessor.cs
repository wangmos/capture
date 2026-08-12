using System.Windows;

namespace WeCapture.Ocr;

/// <summary>
/// DB(Differentiable Binarization) 概率图后处理：二值化 → 连通域 → 最小外接矩形 → 外扩。
/// 参数取自 PP-OCRv6_small_det 的 inference.yml（thresh 0.2 / box_thresh 0.45 / unclip_ratio 1.4）。
/// 用等比外扩替代 PaddleOCR 的 Clipper 多边形偏移：对矩形两者等价，屏幕文本几乎都是轴对齐的。
/// </summary>
internal static class DbPostProcessor
{
    public const float BinaryThreshold = 0.2f;
    public const double BoxScoreThreshold = 0.45;
    public const double UnclipRatio = 1.4;
    private const int MinBoxSide = 3;
    private const int MaxCandidates = 3000;

    /// <summary>从概率图提取文本框（网络输入坐标系）。</summary>
    public static List<(Point[] Quad, double Score)> ExtractBoxes(float[] prob, int w, int h)
    {
        var labels = new int[w * h];
        var result = new List<(Point[], double)>();
        var stack = new Stack<int>();
        var component = new List<int>();
        int label = 0;

        for (int start = 0; start < labels.Length; start++)
        {
            if (labels[start] != 0 || prob[start] < BinaryThreshold) continue;
            if (++label > MaxCandidates) break;

            // 迭代式八邻域漫水，避免深递归
            component.Clear();
            stack.Push(start);
            labels[start] = label;

            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                component.Add(idx);
                int cx = idx % w, cy = idx / w;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if ((uint)ny >= (uint)h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx;
                        if ((uint)nx >= (uint)w || (dx == 0 && dy == 0)) continue;
                        int nidx = ny * w + nx;
                        if (labels[nidx] != 0 || prob[nidx] < BinaryThreshold) continue;
                        labels[nidx] = label;
                        stack.Push(nidx);
                    }
                }
            }

            if (component.Count < 4) continue;

            var pts = new List<Point>(component.Count);
            foreach (int idx in component)
                pts.Add(new Point(idx % w, idx / w));

            var box = MinAreaRect(pts);
            if (box == null || MinSide(box) < MinBoxSide) continue;

            // 与 PaddleOCR 一致：先在未外扩的框上算分，再外扩
            double score = PolygonMeanScore(prob, w, h, box);
            if (score < BoxScoreThreshold) continue;

            var expanded = Unclip(box, UnclipRatio);
            if (MinSide(expanded) < MinBoxSide + 2) continue;

            result.Add((expanded, score));
        }

        return result;
    }

    private static double MinSide(Point[] q) =>
        Math.Min(Dist(q[0], q[1]), Dist(q[1], q[2]));

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ================= 最小外接矩形 =================

    /// <summary>凸包 + 旋转卡壳求最小面积外接矩形，返回顺时针四点（左上→右上→右下→左下）。</summary>
    public static Point[]? MinAreaRect(List<Point> points)
    {
        var hull = ConvexHull(points);
        if (hull.Count < 3) return null;

        double bestArea = double.MaxValue;
        Point[]? best = null;

        for (int i = 0; i < hull.Count; i++)
        {
            Point a = hull[i], b = hull[(i + 1) % hull.Count];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-9) continue;
            double ux = ex / len, uy = ey / len;   // 沿边方向
            double vx = -uy, vy = ux;              // 法线方向

            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in hull)
            {
                double pu = p.X * ux + p.Y * uy;
                double pv = p.X * vx + p.Y * vy;
                if (pu < minU) minU = pu;
                if (pu > maxU) maxU = pu;
                if (pv < minV) minV = pv;
                if (pv > maxV) maxV = pv;
            }

            double area = (maxU - minU) * (maxV - minV);
            if (area >= bestArea) continue;

            bestArea = area;
            best = new[]
            {
                Combine(ux, uy, vx, vy, minU, minV),
                Combine(ux, uy, vx, vy, maxU, minV),
                Combine(ux, uy, vx, vy, maxU, maxV),
                Combine(ux, uy, vx, vy, minU, maxV),
            };
        }

        return best == null ? null : OrderClockwise(best);
    }

    private static Point Combine(double ux, double uy, double vx, double vy, double u, double v) =>
        new(u * ux + v * vx, u * uy + v * vy);

    /// <summary>Andrew monotone chain 凸包（逆时针，不含重复端点）。</summary>
    private static List<Point> ConvexHull(List<Point> pts)
    {
        var sorted = new List<Point>(pts);
        sorted.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));

        var hull = new List<Point>(sorted.Count + 1);
        // 下凸壳 + 上凸壳
        for (int pass = 0; pass < 2; pass++)
        {
            int startCount = hull.Count;
            var seq = pass == 0 ? sorted : Enumerable.Reverse(sorted);
            foreach (var p in seq)
            {
                while (hull.Count - startCount >= 2 &&
                       Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1); // 去掉与另一半重复的端点
        }
        return hull;
    }

    private static double Cross(Point o, Point a, Point b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    /// <summary>把四点整理成 左上→右上→右下→左下。</summary>
    public static Point[] OrderClockwise(Point[] q)
    {
        var byX = q.OrderBy(p => p.X).ToArray();
        Point l1 = byX[0], l2 = byX[1], r1 = byX[2], r2 = byX[3];
        (Point tl, Point bl) = l1.Y <= l2.Y ? (l1, l2) : (l2, l1);
        (Point tr, Point br) = r1.Y <= r2.Y ? (r1, r2) : (r2, r1);
        return new[] { tl, tr, br, bl };
    }

    // ================= 外扩 =================

    /// <summary>
    /// 按 PaddleOCR 的 unclip 公式外扩：d = 面积 × ratio / 周长，四边各外移 d。
    /// 矩形情形与 Clipper 的多边形偏移结果一致。
    /// </summary>
    public static Point[] Unclip(Point[] q, double ratio)
    {
        double w = Dist(q[0], q[1]);
        double h = Dist(q[1], q[2]);
        if (w < 1e-6 || h < 1e-6) return q;

        double d = w * h * ratio / (2 * (w + h));

        // 沿矩形自身的两个轴外移，保持旋转角
        double ux = (q[1].X - q[0].X) / w, uy = (q[1].Y - q[0].Y) / w;
        double vx = (q[3].X - q[0].X) / h, vy = (q[3].Y - q[0].Y) / h;

        return new[]
        {
            new Point(q[0].X - (ux + vx) * d, q[0].Y - (uy + vy) * d),
            new Point(q[1].X + (ux - vx) * d, q[1].Y + (uy - vy) * d),
            new Point(q[2].X + (ux + vx) * d, q[2].Y + (uy + vy) * d),
            new Point(q[3].X - (ux - vx) * d, q[3].Y - (uy - vy) * d),
        };
    }

    // ================= 打分 =================

    /// <summary>四边形内概率均值（扫描线填充，含阈值以下像素，等价于 PaddleOCR 的 box_score_fast）。</summary>
    private static double PolygonMeanScore(float[] prob, int w, int h, Point[] quad)
    {
        int minY = Math.Max(0, (int)Math.Floor(quad.Min(p => p.Y)));
        int maxY = Math.Min(h - 1, (int)Math.Ceiling(quad.Max(p => p.Y)));
        int minX = Math.Max(0, (int)Math.Floor(quad.Min(p => p.X)));
        int maxX = Math.Min(w - 1, (int)Math.Ceiling(quad.Max(p => p.X)));
        if (minY > maxY || minX > maxX) return 0;

        double sum = 0;
        int count = 0;
        Span<double> xs = stackalloc double[4];

        for (int y = minY; y <= maxY; y++)
        {
            double cy = y + 0.5;
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                Point a = quad[i], b = quad[(i + 1) % 4];
                if ((a.Y <= cy && b.Y > cy) || (b.Y <= cy && a.Y > cy))
                    xs[n++] = a.X + (cy - a.Y) / (b.Y - a.Y) * (b.X - a.X);
            }
            if (n < 2) continue;
            if (n > 2) { /* 凸四边形最多两个交点，多出的忽略 */ n = 2; }

            int xa = Math.Max(minX, (int)Math.Round(Math.Min(xs[0], xs[1])));
            int xb = Math.Min(maxX, (int)Math.Round(Math.Max(xs[0], xs[1])));
            int row = y * w;
            for (int x = xa; x <= xb; x++)
            {
                sum += prob[row + x];
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }
}
