using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeCapture.Capture;
using WeCapture.Core;

namespace WeCapture.Annotations;

/// <summary>马赛克源图工厂：选区原图按 16px 块降采样（最近邻），懒加载、会话级缓存。</summary>
public static class MosaicImageFactory
{
    private const int BlockSize = 16;

    /// <summary>从冻结的屏幕缓冲生成选区的马赛克图（物理像素）。</summary>
    public static BitmapSource Create(MonitorSet monitors, RectI sel)
    {
        int w = sel.W, h = sel.H;
        if (w <= 0 || h <= 0)
            throw new ArgumentException("选区为空", nameof(sel));

        // 1) 取选区原 BGRA
        var src = new byte[w * h * 4];
        CopyRegion(monitors, sel, src);

        // 2) 块化：out(x,y) = src(x 对齐到块左上角)
        var dst = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int by = (y / BlockSize) * BlockSize;
            int srcRow = by * w * 4;
            int dstRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int bx = (x / BlockSize) * BlockSize;
                int si = srcRow + bx * 4;
                int di = dstRow + x * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = 255;
            }
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dst, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>把冻结监视器缓冲中 region 区域的像素拷入 dst（BGRA）。</summary>
    internal static void CopyRegion(MonitorSet monitors, RectI region, byte[] dst)
    {
        int dstStride = region.W * 4;
        foreach (var mon in monitors)
        {
            if (!mon.BoundsPx.IntersectsWith(region)) continue;
            var inter = mon.BoundsPx.Intersect(region);
            for (int y = inter.Top; y < inter.Bottom; y++)
            {
                int srcIdx = (y - mon.BoundsPx.Y) * mon.Stride + (inter.Left - mon.BoundsPx.X) * 4;
                int dstIdx = (y - region.Y) * dstStride + (inter.Left - region.X) * 4;
                Buffer.BlockCopy(mon.Bgra, srcIdx, dst, dstIdx, inter.W * 4);
            }
        }
    }
}
