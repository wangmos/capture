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

    /// <summary>Bgra32 像素缓冲（取色用）。</summary>
    public required byte[] Bgra { get; init; }

    public int Stride => BoundsPx.W * 4;

    /// <summary>O(1) 取全局像素颜色；越界返回透明。</summary>
    public Color GetPixel(int gx, int gy)
    {
        int lx = gx - BoundsPx.X;
        int ly = gy - BoundsPx.Y;
        if ((uint)lx >= (uint)BoundsPx.W || (uint)ly >= (uint)BoundsPx.H)
            return Colors.Transparent;

        int idx = ly * Stride + lx * 4;
        byte[] b = Bgra;
        return Color.FromArgb(b[idx + 3], b[idx + 2], b[idx + 1], b[idx]);
    }
}
