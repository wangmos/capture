namespace WeCapture.Core;

/// <summary>整数矩形（虚拟屏物理像素坐标，原点可为负）。全局唯一真值坐标系。</summary>
public struct RectI
{
    public int X;
    public int Y;
    public int W;
    public int H;

    public RectI(int x, int y, int w, int h)
    {
        X = x; Y = y; W = w; H = h;
    }

    public readonly int Left => X;
    public readonly int Top => Y;
    public readonly int Right => X + W;
    public readonly int Bottom => Y + H;
    public readonly PointI Location => new(X, Y);
    public readonly PointI Center => new(X + W / 2, Y + H / 2);
    public readonly bool IsEmpty => W <= 0 || H <= 0;

    public static RectI FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);

    /// <summary>规范化（宽高非负）。</summary>
    public static RectI Normalize(PointI a, PointI b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        return new RectI(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    public readonly bool Contains(PointI p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public readonly bool IntersectsWith(RectI other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public readonly RectI Intersect(RectI other)
    {
        int l = Math.Max(Left, other.Left);
        int t = Math.Max(Top, other.Top);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return r > l && b > t ? FromLTRB(l, t, r, b) : default;
    }

    public readonly RectI Offset(int dx, int dy) => new(X + dx, Y + dy, W, H);

    /// <summary>包围两个矩形的最小矩形。</summary>
    public readonly RectI Union(RectI other) => FromLTRB(
        Math.Min(Left, other.Left), Math.Min(Top, other.Top),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    /// <summary>把本矩形限制在 bounds 内（尺寸不变，位置平移）。</summary>
    public readonly RectI ClampInto(RectI bounds)
    {
        int x = X, y = Y;
        if (W >= bounds.W) x = bounds.X;
        else x = Math.Clamp(x, bounds.Left, bounds.Right - W);
        if (H >= bounds.H) y = bounds.Y;
        else y = Math.Clamp(y, bounds.Top, bounds.Bottom - H);
        return new RectI(x, y, W, H);
    }

    public override readonly string ToString() => $"{X},{Y} {W}x{H}";

    public override readonly bool Equals(object? obj) =>
        obj is RectI r && r.X == X && r.Y == Y && r.W == W && r.H == H;

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, W, H);

    public static bool operator ==(RectI a, RectI b) => a.Equals(b);
    public static bool operator !=(RectI a, RectI b) => !a.Equals(b);
}

public struct PointI
{
    public int X;
    public int Y;

    public PointI(int x, int y)
    {
        X = x; Y = y;
    }

    public static implicit operator System.Windows.Point(PointI p) => new(p.X, p.Y);

    public override readonly string ToString() => $"{X},{Y}";
}
