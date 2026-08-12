using WeCapture.Core;

namespace WeCapture.Session;

/// <summary>选区命中测试：8 个缩放手柄 &gt; 内部 &gt; 外部。</summary>
public static class SelectionHitTester
{
    /// <summary>手柄视觉尺寸（物理 px）。</summary>
    public const int HandleSize = 7;

    /// <summary>手柄命中半径（物理 px）。</summary>
    public const int HandleHitRadius = 6;

    public static DragMode HitTest(RectI sel, PointI p)
    {
        int cx = sel.X + sel.W / 2;
        int cy = sel.Y + sel.H / 2;

        if (Near(p, sel.Left, sel.Top)) return DragMode.ResizeTopLeft;
        if (Near(p, cx, sel.Top)) return DragMode.ResizeTop;
        if (Near(p, sel.Right, sel.Top)) return DragMode.ResizeTopRight;
        if (Near(p, sel.Right, cy)) return DragMode.ResizeRight;
        if (Near(p, sel.Right, sel.Bottom)) return DragMode.ResizeBottomRight;
        if (Near(p, cx, sel.Bottom)) return DragMode.ResizeBottom;
        if (Near(p, sel.Left, sel.Bottom)) return DragMode.ResizeBottomLeft;
        if (Near(p, sel.Left, cy)) return DragMode.ResizeLeft;

        return sel.Contains(p) ? DragMode.Move : DragMode.None;
    }

    private static bool Near(PointI p, int x, int y) =>
        Math.Abs(p.X - x) <= HandleHitRadius && Math.Abs(p.Y - y) <= HandleHitRadius;

    /// <summary>手柄中心点（本屏局部或全局坐标均可，按传入 sel 计算）。</summary>
    public static PointI[] HandlePoints(RectI sel)
    {
        int cx = sel.X + sel.W / 2;
        int cy = sel.Y + sel.H / 2;
        return new[]
        {
            new PointI(sel.Left, sel.Top),
            new PointI(cx, sel.Top),
            new PointI(sel.Right, sel.Top),
            new PointI(sel.Right, cy),
            new PointI(sel.Right, sel.Bottom),
            new PointI(cx, sel.Bottom),
            new PointI(sel.Left, sel.Bottom),
            new PointI(sel.Left, cy),
        };
    }
}
