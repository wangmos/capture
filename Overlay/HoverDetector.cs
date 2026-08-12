using System.Windows.Automation;
using WeCapture.Core;
using WeCapture.Native;

namespace WeCapture.Overlay;

/// <summary>
/// 窗口/控件智能探测（微信式悬停高亮）。
/// Win32 为主：EnumWindows Z 序命中顶层窗 → RealChildWindowFromPoint 下钻子控件；
/// UIA FromPoint 尽力增强（取更细的控件边界）；候选需稳定 60ms 才切换高亮。
/// </summary>
public sealed class HoverDetector
{
    private const int StableMs = 60;
    private const int MaxDrillDepth = 8;

    private readonly HashSet<IntPtr> _excludeHwnds = new();

    private RectI? _candidate;
    private DateTime _candidateSince;
    private RectI? _committed;

    /// <summary>排除覆盖层自身的窗口句柄。</summary>
    public void AddExcludeHwnd(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            _excludeHwnds.Add(hwnd);
    }

    public void Reset()
    {
        _candidate = null;
        _committed = null;
    }

    /// <summary>返回当前应高亮的矩形（含防抖）。</summary>
    public RectI? Detect(PointI gpt)
    {
        RectI? c = DetectCore(gpt);

        if (c == _committed)
            return _committed;

        var now = DateTime.Now;
        if (c != _candidate)
        {
            _candidate = c;
            _candidateSince = now;
            return _committed;
        }

        if ((now - _candidateSince).TotalMilliseconds >= StableMs)
        {
            _committed = c;
            return c;
        }
        return _committed;
    }

    private RectI? DetectCore(PointI gpt)
    {
        var win32 = DetectWin32(gpt);
        var uia = DetectUia(gpt);

        // UIA 结果只有比 Win32 结果更精细（被包含）时才采用；
        // 若 UIA 命中的是覆盖层自身（整屏大矩形），包含检查会自然过滤掉。
        if (uia is RectI u && u.W > 1 && u.H > 1 && u.Contains(gpt))
        {
            if (win32 is RectI w && (u == w || ContainedIn(u, w)))
                return u;
            if (win32 == null)
                return u;
        }
        return win32;
    }

    private static bool ContainedIn(RectI inner, RectI outer) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top &&
        inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    // ---------- Win32 ----------

    private RectI? DetectWin32(PointI gpt)
    {
        IntPtr found = IntPtr.Zero;
        var pt = new POINT { X = gpt.X, Y = gpt.Y };

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (_excludeHwnds.Contains(hwnd)) return true;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out RECT r)) return true;
            if (r.Width <= 0 || r.Height <= 0) return true;
            var rect = RectI.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (!rect.Contains(gpt)) return true;

            found = hwnd;
            return false; // Z 序顶→底，第一个命中即最顶层
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
            return null;

        // 向子控件下钻（每轮都从屏幕坐标重新换算，避免累积换算误差）
        IntPtr cur = found;
        for (int depth = 0; depth < MaxDrillDepth; depth++)
        {
            POINT client = pt;
            NativeMethods.ScreenToClient(cur, ref client);
            IntPtr child = NativeMethods.RealChildWindowFromPoint(cur, client);
            if (child == IntPtr.Zero || child == cur) break;
            if (!NativeMethods.IsWindowVisible(child)) break;
            cur = child;
        }

        if (NativeMethods.GetWindowRect(cur, out RECT fr) && fr.Width > 0 && fr.Height > 0)
            return RectI.FromLTRB(fr.Left, fr.Top, fr.Right, fr.Bottom);
        return null;
    }

    // ---------- UIA（尽力增强，任何异常都静默降级） ----------

    private static RectI? DetectUia(PointI gpt)
    {
        try
        {
            var el = AutomationElement.FromPoint(new System.Windows.Point(gpt.X, gpt.Y));
            if (el == null) return null;

            var br = el.Current.BoundingRectangle;
            if (br.IsEmpty || double.IsNaN(br.Width) || double.IsNaN(br.Height)) return null;

            int x = (int)Math.Round(br.X);
            int y = (int)Math.Round(br.Y);
            int w = (int)Math.Round(br.Width);
            int h = (int)Math.Round(br.Height);
            if (w <= 0 || h <= 0) return null;

            return new RectI(x, y, w, h);
        }
        catch
        {
            return null;
        }
    }
}
