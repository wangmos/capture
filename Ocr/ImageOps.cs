using System.Windows;

namespace WeCapture.Ocr;

/// <summary>OCR 用的纯像素运算：双线性缩放、归一化张量、四点裁剪。全部在 BGRA32 缓冲上进行。</summary>
internal static class ImageOps
{
    /// <summary>
    /// 双线性缩放并写入 CHW float 张量：dst[c][y][x] = (v/255 - mean[c]) / std[c]。
    /// 通道顺序沿用 PaddleOCR 的 BGR（det 的 inference.yml 明确 img_mode: BGR）。
    /// </summary>
    public static float[] ToNormalizedChw(
        byte[] bgra, int srcW, int srcH, int dstW, int dstH,
        ReadOnlySpan<float> mean, ReadOnlySpan<float> std, bool swapToRgb)
    {
        var dst = new float[3 * dstW * dstH];
        int plane = dstW * dstH;

        // 缩放系数：按像素中心对齐，避免整体偏移半个像素
        double sx = (double)srcW / dstW;
        double sy = (double)srcH / dstH;

        for (int y = 0; y < dstH; y++)
        {
            double fy = (y + 0.5) * sy - 0.5;
            int y0 = (int)Math.Floor(fy);
            double wy = fy - y0;
            int y0c = Math.Clamp(y0, 0, srcH - 1);
            int y1c = Math.Clamp(y0 + 1, 0, srcH - 1);
            int row0 = y0c * srcW * 4, row1 = y1c * srcW * 4;
            int dstRow = y * dstW;

            for (int x = 0; x < dstW; x++)
            {
                double fx = (x + 0.5) * sx - 0.5;
                int x0 = (int)Math.Floor(fx);
                double wx = fx - x0;
                int x0c = Math.Clamp(x0, 0, srcW - 1) * 4;
                int x1c = Math.Clamp(x0 + 1, 0, srcW - 1) * 4;

                for (int c = 0; c < 3; c++)
                {
                    double v00 = bgra[row0 + x0c + c], v01 = bgra[row0 + x1c + c];
                    double v10 = bgra[row1 + x0c + c], v11 = bgra[row1 + x1c + c];
                    double top = v00 + (v01 - v00) * wx;
                    double bot = v10 + (v11 - v10) * wx;
                    double v = top + (bot - top) * wy;

                    // BGRA 缓冲里 c=0 是 B；需要 RGB 时把 0/2 通道对调
                    int oc = swapToRgb ? 2 - c : c;
                    dst[oc * plane + dstRow + x] = (float)((v / 255.0 - mean[oc]) / std[oc]);
                }
            }
        }
        return dst;
    }

    /// <summary>
    /// 从四点框裁出一块矩形图像（BGRA）。角点按双线性映射采样，
    /// 对屏幕截图这类轴对齐/近轴对齐文本等价于精确透视裁剪。
    /// </summary>
    public static OcrImage CropQuad(OcrImage img, Point[] quad, int outW, int outH)
    {
        var dst = new byte[outW * outH * 4];
        // quad 顺序：左上、右上、右下、左下
        Point p0 = quad[0], p1 = quad[1], p2 = quad[2], p3 = quad[3];

        for (int y = 0; y < outH; y++)
        {
            double v = outH == 1 ? 0 : (double)y / (outH - 1);
            // 左右边界上按 v 插值，再在两点之间按 u 插值
            double lx = p0.X + (p3.X - p0.X) * v, ly = p0.Y + (p3.Y - p0.Y) * v;
            double rx = p1.X + (p2.X - p1.X) * v, ry = p1.Y + (p2.Y - p1.Y) * v;
            int dstRow = y * outW * 4;

            for (int x = 0; x < outW; x++)
            {
                double u = outW == 1 ? 0 : (double)x / (outW - 1);
                double sxf = lx + (rx - lx) * u;
                double syf = ly + (ry - ly) * u;
                SampleBilinear(img, sxf, syf, dst, dstRow + x * 4);
            }
        }
        return new OcrImage { Bgra = dst, Width = outW, Height = outH };
    }

    private static void SampleBilinear(OcrImage img, double fx, double fy, byte[] dst, int di)
    {
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double wx = fx - x0, wy = fy - y0;
        int x0c = Math.Clamp(x0, 0, img.Width - 1), x1c = Math.Clamp(x0 + 1, 0, img.Width - 1);
        int y0c = Math.Clamp(y0, 0, img.Height - 1), y1c = Math.Clamp(y0 + 1, 0, img.Height - 1);
        int r0 = y0c * img.Stride, r1 = y1c * img.Stride;
        var s = img.Bgra;

        for (int c = 0; c < 4; c++)
        {
            double v00 = s[r0 + x0c * 4 + c], v01 = s[r0 + x1c * 4 + c];
            double v10 = s[r1 + x0c * 4 + c], v11 = s[r1 + x1c * 4 + c];
            double top = v00 + (v01 - v00) * wx;
            double bot = v10 + (v11 - v10) * wx;
            dst[di + c] = (byte)Math.Clamp(top + (bot - top) * wy + 0.5, 0, 255);
        }
    }

    /// <summary>平均亮度（抽样），用于暗色主题判定。</summary>
    public static double AverageLuma(OcrImage img)
    {
        long sum = 0;
        int n = 0;
        var px = img.Bgra;
        for (int i = 0; i + 2 < px.Length; i += 64)
        {
            sum += (px[i] + (px[i + 1] << 1) + px[i + 2]) >> 2;
            n++;
        }
        return n == 0 ? 255 : sum / (double)n;
    }

    /// <summary>整体反色（暗底浅字转为浅底深字，识别模型对后者更稳）。</summary>
    public static OcrImage Invert(OcrImage img)
    {
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i + 2 < px.Length; i += 4)
        {
            px[i] = (byte)(255 - px[i]);
            px[i + 1] = (byte)(255 - px[i + 1]);
            px[i + 2] = (byte)(255 - px[i + 2]);
        }
        return new OcrImage { Bgra = px, Width = img.Width, Height = img.Height };
    }
}
