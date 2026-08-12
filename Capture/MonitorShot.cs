using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeCapture.Capture;

/// <summary>单个显示器的冻结截图及元数据。</summary>
public sealed class MonitorShot
{
    public required IntPtr Handle { get; init; }
    public required string DeviceName { get; init; }

    /// <summary>显示器在虚拟屏中的矩形（物理像素，原点可为负）。</summary>
    public required Core.RectI BoundsPx { get; init; }

    public required Core.RectI WorkAreaPx { get; init; }
    public required bool IsPrimary { get; init; }

    /// <summary>DPI 缩放（1.0 = 96dpi = 100%）。</summary>
    public required double DpiScale { get; init; }

    /// <summary>冻结的屏幕图像（已 Freeze）。</summary>
    public required BitmapSource Image { get; init; }

    /// <summary>
    /// Bgra32 全屏像素缓冲（取色/马赛克用）。懒加载：整屏转换一次要几十毫秒，
    /// 放在截图的关键路径上会直接推迟覆盖层出现的时间。
    /// </summary>
    public byte[] Bgra => _bgra.Value;

    /// <summary>缓冲是否已就绪（未就绪时取色走单像素读取）。</summary>
    public bool IsBgraReady => _bgra.IsValueCreated;

    private Lazy<byte[]> _bgra = null!;

    public int Stride => BoundsPx.W * 4;

    /// <summary>构造后立即调用，装配懒加载的像素缓冲。</summary>
    public void InitBuffer()
    {
        var image = Image;
        int w = BoundsPx.W, h = BoundsPx.H;
        _bgra = new Lazy<byte[]>(() =>
        {
            var conv = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var buf = new byte[w * 4 * h];
            conv.CopyPixels(buf, w * 4, 0);
            return buf;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>取全局像素颜色；越界返回透明。</summary>
    public Color GetPixel(int gx, int gy)
    {
        int lx = gx - BoundsPx.X;
        int ly = gy - BoundsPx.Y;
        if ((uint)lx >= (uint)BoundsPx.W || (uint)ly >= (uint)BoundsPx.H)
            return Colors.Transparent;

        // 缓冲还没建好时只读这一个像素，不为了取色去触发整屏转换
        if (!IsBgraReady)
        {
            var px = new byte[4];
            try
            {
                var conv = new FormatConvertedBitmap(Image, PixelFormats.Bgra32, null, 0);
                conv.CopyPixels(new System.Windows.Int32Rect(lx, ly, 1, 1), px, 4, 0);
            }
            catch
            {
                return Colors.Transparent;
            }
            return Color.FromArgb(px[3], px[2], px[1], px[0]);
        }

        int idx = ly * Stride + lx * 4;
        byte[] b = Bgra;
        return Color.FromArgb(b[idx + 3], b[idx + 2], b[idx + 1], b[idx]);
    }
}
