using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WeCapture.Native;

namespace WeCapture.Core;

/// <summary>运行时绘制应用图标（微信绿圆角方块 + 白色截图取景框），无需外部资源文件。</summary>
public static class IconFactory
{
    private static Icon? _trayIcon;
    private static Icon? _windowIcon;
    private static System.Windows.Media.ImageSource? _wpfIcon;

    public static Icon TrayIcon => _trayIcon ??= CreateIcon(16);
    public static Icon WindowIcon => _windowIcon ??= CreateIcon(32);

    /// <summary>供 WPF 窗口的 Icon 属性使用（Window.Icon 要的是 ImageSource，不是 GDI Icon）。</summary>
    public static System.Windows.Media.ImageSource WpfIcon => _wpfIcon ??= CreateWpfIcon(64);

    private static System.Windows.Media.ImageSource CreateWpfIcon(int size)
    {
        using var bmp = DrawIcon(size);

        // 逐像素搬运而不是走 HBITMAP：GetHbitmap 会丢掉透明通道，圆角外面会变成黑块。
        // GDI+ 的 Format32bppArgb 与 WPF 的 Bgra32 都是非预乘 alpha，可直接对应。
        var data = bmp.LockBits(new Rectangle(0, 0, size, size),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var src = System.Windows.Media.Imaging.BitmapSource.Create(
                size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * size, data.Stride);
            src.Freeze();
            return src;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Icon CreateIcon(int size)
    {
        using var bmp = DrawIcon(size);

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            // 复制为托管 Icon 后立即释放原始句柄
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>把图标画进一张位图（托盘图标、窗口图标、WPF 图标共用同一套绘制）。</summary>
    private static Bitmap DrawIcon(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float u = size / 32f; // 以 32px 为基准换算

            // 微信绿圆角背景
            using (var bg = new SolidBrush(Color.FromArgb(7, 193, 96)))
            {
                float r = 7 * u;
                var rect = new RectangleF(0, 0, size, size);
                using var path = RoundedRect(rect, r);
                g.FillPath(bg, path);
            }

            // 白色取景框（四角括号）
            using (var pen = new Pen(Color.White, Math.Max(1.6f, 2.4f * u)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float m = 7.5f * u;      // 边距
                float arm = 6.5f * u;    // 括号臂长
                // 左上
                g.DrawLine(pen, m, m + arm, m, m);
                g.DrawLine(pen, m, m, m + arm, m);
                // 右上
                g.DrawLine(pen, size - m - arm, m, size - m, m);
                g.DrawLine(pen, size - m, m, size - m, m + arm);
                // 右下
                g.DrawLine(pen, size - m, size - m - arm, size - m, size - m);
                g.DrawLine(pen, size - m, size - m, size - m - arm, size - m);
                // 左下
                g.DrawLine(pen, m + arm, size - m, m, size - m);
                g.DrawLine(pen, m, size - m, m, size - m - arm);
            }

            // 中心小圆点（镜头感）
            using (var dot = new SolidBrush(Color.White))
            {
                float d = 4.5f * u;
                g.FillEllipse(dot, (size - d) / 2, (size - d) / 2, d, d);
            }
        }
        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
