using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WeCapture.Core;
using WeCapture.Native;

namespace WeCapture.Capture;

/// <summary>
/// 全部显示器的冻结截图集合。
/// 逐显示器 CreateDC + BitBlt（物理像素、各屏独立 DPI），拼成虚拟屏真值。
/// </summary>
public sealed class MonitorSet : IEnumerable<MonitorShot>
{
    private readonly List<MonitorShot> _shots;

    public IReadOnlyList<MonitorShot> Monitors => _shots;

    /// <summary>虚拟屏整体边界（物理像素）。</summary>
    public RectI VirtualBounds { get; }

    private MonitorSet(List<MonitorShot> shots)
    {
        _shots = shots;
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var s in shots)
        {
            l = Math.Min(l, s.BoundsPx.Left);
            t = Math.Min(t, s.BoundsPx.Top);
            r = Math.Max(r, s.BoundsPx.Right);
            b = Math.Max(b, s.BoundsPx.Bottom);
        }
        VirtualBounds = shots.Count > 0 ? RectI.FromLTRB(l, t, r, b) : default;
    }

    public static MonitorSet CaptureAll()
    {
        var shots = new List<MonitorShot>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                shots.Add(CaptureMonitor(hMon));
                return true;
            }, IntPtr.Zero);

        if (shots.Count == 0)
            throw new InvalidOperationException("未检测到任何显示器");

        return new MonitorSet(shots);
    }

    private static MonitorShot CaptureMonitor(IntPtr hMon)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(hMon, ref mi))
            throw new InvalidOperationException("GetMonitorInfo 失败");

        NativeMethods.GetDpiForMonitor(hMon, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);

        var bounds = new RectI(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height);
        var work = new RectI(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Width, mi.rcWork.Height);
        bool primary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;

        int w = bounds.W, h = bounds.H;
        IntPtr srcDc = IntPtr.Zero, memDc = IntPtr.Zero, hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            srcDc = NativeMethods.CreateDC("DISPLAY", mi.szDevice, null, IntPtr.Zero);
            if (srcDc == IntPtr.Zero)
                throw new InvalidOperationException($"CreateDC 失败: {mi.szDevice}");

            memDc = NativeMethods.CreateCompatibleDC(srcDc);
            hBmp = NativeMethods.CreateCompatibleBitmap(srcDc, w, h);
            oldBmp = NativeMethods.SelectObject(memDc, hBmp);

            // CAPTUREBLT 以捕获分层窗口（如某些半透明窗口）
            if (!NativeMethods.BitBlt(memDc, 0, 0, w, h, srcDc, 0, 0,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new InvalidOperationException("BitBlt 失败");

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();

            var shot = new MonitorShot
            {
                Handle = hMon,
                DeviceName = mi.szDevice,
                BoundsPx = bounds,
                WorkAreaPx = work,
                IsPrimary = primary,
                DpiScale = dpiX / 96.0,
                Image = image,
            };
            shot.InitBuffer();
            return shot;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldBmp);
            if (hBmp != IntPtr.Zero) NativeMethods.DeleteObject(hBmp);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            if (srcDc != IntPtr.Zero) NativeMethods.DeleteDC(srcDc);
        }
    }

    public MonitorShot? FindMonitorContaining(PointI p) =>
        _shots.FirstOrDefault(s => s.BoundsPx.Contains(p));

    /// <summary>跨显示器取色（全局坐标）。</summary>
    public System.Windows.Media.Color GetPixel(int gx, int gy) =>
        FindMonitorContaining(new PointI(gx, gy))?.GetPixel(gx, gy) ?? System.Windows.Media.Colors.Transparent;

    public IEnumerator<MonitorShot> GetEnumerator() => _shots.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        _shots.Clear();
    }
}
