using System.Windows;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WeCapture.Core;

namespace WeCapture.Ocr;

/// <summary>文本检测（PP-OCRv6_det_small，DB）：图像 → 文本行四点框。</summary>
internal static class TextDetector
{
    /// <summary>
    /// 总像素上限：控制推理耗时。
    /// 不能按"长边"限制——长截图（如 645x5671）按长边缩放会被压成 174x1536，
    /// 文字小到完全检不出来。按面积缩放则细长图几乎不损失分辨率。
    /// </summary>
    private const long MaxPixels = 3_500_000;

    /// <summary>单边硬上限（模型的动态形状上限为 4000）。</summary>
    private const int MaxDim = 4000;

    /// <summary>长边低于此值时放大（截图里的小字放大后检出率明显更高）。</summary>
    private const int UpscaleBelow = 640;

    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    public static List<OcrLine> Detect(OcrImage img, bool swapToRgb)
    {
        int maxSide = Math.Max(img.Width, img.Height);
        long pixels = (long)img.Width * img.Height;
        double scale = 1.0;

        if (pixels > MaxPixels)
            scale = Math.Sqrt((double)MaxPixels / pixels);
        else if (maxSide < UpscaleBelow && maxSide > 0)
            scale = Math.Min(2.0, (double)UpscaleBelow * 2 / maxSide);

        // 单边不得越过模型的动态形状上限
        if (img.Width * scale > MaxDim) scale = (double)MaxDim / img.Width;
        if (img.Height * scale > MaxDim) scale = (double)MaxDim / img.Height;

        int netW = Align32(img.Width * scale);
        int netH = Align32(img.Height * scale);

        var input = ImageOps.ToNormalizedChw(img.Bgra, img.Width, img.Height, netW, netH, Mean, Std, swapToRgb);
        var tensor = new DenseTensor<float>(input, new[] { 1, 3, netH, netW });

        var session = OnnxModels.Det;
        string inputName = session.InputMetadata.Keys.First();

        float[] prob;
        using (var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) }))
        {
            var output = results.First().AsTensor<float>();
            prob = output.ToArray();
        }

        var raw = DbPostProcessor.ExtractBoxes(prob, netW, netH);

        // 网络坐标 → 图像坐标（x/y 分别换算：对齐到 32 后长宽比略有变化）
        double kx = (double)img.Width / netW;
        double ky = (double)img.Height / netH;

        var lines = new List<OcrLine>(raw.Count);
        foreach (var (quad, score) in raw)
        {
            var mapped = new Point[4];
            for (int i = 0; i < 4; i++)
                mapped[i] = new Point(
                    Math.Clamp(quad[i].X * kx, 0, img.Width),
                    Math.Clamp(quad[i].Y * ky, 0, img.Height));

            var bounds = new Rect(
                mapped.Min(p => p.X), mapped.Min(p => p.Y),
                Math.Max(1, mapped.Max(p => p.X) - mapped.Min(p => p.X)),
                Math.Max(1, mapped.Max(p => p.Y) - mapped.Min(p => p.Y)));

            lines.Add(new OcrLine { Quad = mapped, Bounds = bounds, DetScore = score });
        }

        TraceLog.Log($"OCR det {img.Width}x{img.Height} -> net {netW}x{netH}, boxes={lines.Count}");
        return SortReadingOrder(lines);
    }

    private static int Align32(double v)
    {
        int n = (int)Math.Round(v / 32.0) * 32;
        return Math.Max(32, n);
    }

    /// <summary>按阅读顺序排序：先按行聚类（纵向重叠过半即同一行），行内再按 x 递增。</summary>
    public static List<OcrLine> SortReadingOrder(List<OcrLine> lines)
    {
        var rest = lines.OrderBy(l => l.Bounds.Top).ToList();
        var ordered = new List<OcrLine>(lines.Count);

        while (rest.Count > 0)
        {
            var seed = rest[0];
            rest.RemoveAt(0);
            var row = new List<OcrLine> { seed };
            double top = seed.Bounds.Top, bottom = seed.Bounds.Bottom;

            for (int i = rest.Count - 1; i >= 0; i--)
            {
                var c = rest[i];
                double overlap = Math.Min(bottom, c.Bounds.Bottom) - Math.Max(top, c.Bounds.Top);
                if (overlap > 0.5 * Math.Min(bottom - top, c.Bounds.Height))
                {
                    row.Add(c);
                    rest.RemoveAt(i);
                }
            }

            row.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));
            ordered.AddRange(row);
        }

        return ordered;
    }
}
