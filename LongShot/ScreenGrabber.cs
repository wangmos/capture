using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WeCapture.Core;
using WeCapture.Native;

namespace WeCapture.LongShot;

/// <summary>
/// 实时抓取虚拟屏上的一块区域（物理像素）。
/// 长截图必须看到滚动中的真实画面，不能用会话开始时那份冻结截图。
/// </summary>
internal static class ScreenGrabber
{
    /// <summary>抓取区域并返回 BGRA32 紧凑缓冲。</summary>
    public static byte[] CaptureRegion(RectI region)
    {
        if (region.W <= 0 || region.H <= 0)
            throw new ArgumentException("区域为空", nameof(region));

        IntPtr srcDc = IntPtr.Zero, memDc = IntPtr.Zero, hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            // "DISPLAY" 设备 DC 覆盖整个虚拟桌面，源坐标可为负
            srcDc = NativeMethods.CreateDC("DISPLAY", null!, null, IntPtr.Zero);
            if (srcDc == IntPtr.Zero) throw new InvalidOperationException("CreateDC 失败");

            memDc = NativeMethods.CreateCompatibleDC(srcDc);
            hBmp = NativeMethods.CreateCompatibleBitmap(srcDc, region.W, region.H);
            oldBmp = NativeMethods.SelectObject(memDc, hBmp);

            if (!NativeMethods.BitBlt(memDc, 0, 0, region.W, region.H, srcDc, region.X, region.Y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new InvalidOperationException("BitBlt 失败");

            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var conv = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            var buf = new byte[region.W * 4 * region.H];
            conv.CopyPixels(buf, region.W * 4, 0);
            return buf;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldBmp);
            if (hBmp != IntPtr.Zero) NativeMethods.DeleteObject(hBmp);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            if (srcDc != IntPtr.Zero) NativeMethods.DeleteDC(srcDc);
        }
    }

    /// <summary>把 BGRA 缓冲转成可显示/导出的位图。</summary>
    public static BitmapSource ToBitmap(byte[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>两帧是否完全一致（帧稳定判定）。</summary>
    public static bool SameFrame(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return a.AsSpan().SequenceEqual(b);
    }

    /// <summary>把滚轮送到指定屏幕位置（先移动光标，滚轮跟随光标下的窗口）。</summary>
    public static void SendWheel(PointI at, int notches)
    {
        NativeMethods.SetCursorPos(at.X, at.Y);

        var input = new INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                    mouseData = unchecked((uint)(notches * NativeMethods.WHEEL_DELTA)),
                },
            },
        };

        uint sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
            TraceLog.Log($"SendWheel failed err={Marshal.GetLastWin32Error()}");
    }
}
