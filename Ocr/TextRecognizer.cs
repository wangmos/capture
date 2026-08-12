using System.Windows;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WeCapture.Ocr;

/// <summary>
/// 文本识别（PP-OCRv6_rec_small，CTC）：文本行 → 文字 + 逐字符横向区间。
/// 字符区间由 CTC 时间步反推——时间步与输入宽度线性对应，是"图上选字"的定位依据。
/// </summary>
internal static class TextRecognizer
{
    /// <summary>模型输入固定高度（inference.yml: image_shape [3,48,320]）。</summary>
    public const int InputHeight = 48;

    private const int MaxWidth = 3200;
    private const int BatchSize = 8;

    private static readonly float[] Mean = { 0.5f, 0.5f, 0.5f };
    private static readonly float[] Std = { 0.5f, 0.5f, 0.5f };

    /// <summary>对检测出的每一行做识别，结果写回 OcrLine。</summary>
    public static void RecognizeLines(OcrImage img, IReadOnlyList<OcrLine> lines, bool swapToRgb)
    {
        if (lines.Count == 0) return;

        // 先按裁剪后的宽度排序，宽度相近的分到一批，减少 padding 浪费
        var crops = new (int Index, OcrImage Crop)[lines.Count];
        for (int i = 0; i < lines.Count; i++)
            crops[i] = (i, CropLine(img, lines[i]));

        var order = crops.OrderBy(c => c.Crop.Width).ToArray();

        for (int start = 0; start < order.Length; start += BatchSize)
        {
            var batch = order.Skip(start).Take(BatchSize).ToArray();
            RunBatch(batch, lines, swapToRgb);
        }
    }

    /// <summary>整块图像按单行识别（固定区域快路径：跳过检测，直接送 rec）。</summary>
    public static (string Text, double Score, IReadOnlyList<OcrChar> Chars) RecognizeSingle(OcrImage img, bool swapToRgb)
    {
        int cw = ContentWidth(img.Width, img.Height);
        var resized = ImageOps.ToNormalizedChw(img.Bgra, img.Width, img.Height, cw, InputHeight, Mean, Std, swapToRgb);
        var tensor = new DenseTensor<float>(resized, new[] { 1, 3, InputHeight, cw });

        var logits = Run(tensor, out int t, out int classes);
        var (text, score, chars) = Decode(logits, 0, t, classes, cw, cw, 0, img.Width);
        return (text, score, chars);
    }

    private static void RunBatch((int Index, OcrImage Crop)[] batch, IReadOnlyList<OcrLine> lines, bool swapToRgb)
    {
        int padded = 0;
        var widths = new int[batch.Length];
        for (int i = 0; i < batch.Length; i++)
        {
            widths[i] = ContentWidth(batch[i].Crop.Width, batch[i].Crop.Height);
            padded = Math.Max(padded, widths[i]);
        }

        int plane = InputHeight * padded;
        var buffer = new float[batch.Length * 3 * plane];   // 归一化空间的 0 即中性灰，等价于 PaddleOCR 的零填充

        for (int b = 0; b < batch.Length; b++)
        {
            int cw = widths[b];
            var crop = batch[b].Crop;
            var norm = ImageOps.ToNormalizedChw(crop.Bgra, crop.Width, crop.Height, cw, InputHeight, Mean, Std, swapToRgb);
            for (int c = 0; c < 3; c++)
                for (int y = 0; y < InputHeight; y++)
                    Array.Copy(norm, (c * InputHeight + y) * cw,
                               buffer, ((b * 3 + c) * InputHeight + y) * padded, cw);
        }

        var tensor = new DenseTensor<float>(buffer, new[] { batch.Length, 3, InputHeight, padded });
        var logits = Run(tensor, out int steps, out int classes);

        for (int b = 0; b < batch.Length; b++)
        {
            var line = lines[batch[b].Index];
            var (text, score, chars) = Decode(logits, b, steps, classes, padded, widths[b],
                                              line.Bounds.Left, line.Bounds.Width);
            line.Text = text;
            line.RecScore = score;
            line.Chars = chars;
        }
    }

    private static float[] Run(DenseTensor<float> input, out int steps, out int classes)
    {
        var session = OnnxModels.Rec;
        string inputName = session.InputMetadata.Keys.First();
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
        var output = results.First().AsTensor<float>();
        steps = output.Dimensions[1];
        classes = output.Dimensions[2];
        return output.ToArray();
    }

    /// <summary>按高 48 等比缩放后的宽度。</summary>
    private static int ContentWidth(int w, int h)
    {
        if (h <= 0) return InputHeight;
        int cw = (int)Math.Ceiling((double)InputHeight * w / h);
        return Math.Clamp(cw, 8, MaxWidth);
    }

    /// <summary>沿四点框裁出文本行，高度归一到 48。</summary>
    private static OcrImage CropLine(OcrImage img, OcrLine line)
    {
        double w = Distance(line.Quad[0], line.Quad[1]);
        double h = Distance(line.Quad[1], line.Quad[2]);
        int outH = InputHeight;
        int outW = Math.Clamp((int)Math.Ceiling(w / Math.Max(h, 1) * outH), 8, MaxWidth);
        return ImageOps.CropQuad(img, line.Quad, outW, outH);
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// CTC 贪心解码。合并规则同 PaddleOCR：跳过 blank(0)，并合并与前一步相同的索引。
    /// 每个字符的时间步区间线性映射回图像横坐标，得到可用于选字的字符边界。
    /// </summary>
    private static (string Text, double Score, IReadOnlyList<OcrChar> Chars) Decode(
        float[] logits, int batchIndex, int steps, int classes,
        int paddedWidth, int contentWidth, double originX, double spanWidth)
    {
        var charset = OnnxModels.Characters;
        var sb = new System.Text.StringBuilder();
        var chars = new List<OcrChar>();

        double scoreSum = 0;
        int scoreCount = 0;
        int baseOffset = batchIndex * steps * classes;

        // padding 区域不含内容，只有 contentWidth 之前的时间步有效
        double stepWidth = (double)paddedWidth / steps;
        int validSteps = Math.Clamp((int)Math.Ceiling(contentWidth / stepWidth), 1, steps);

        // 逐时间步取 argmax，按"游程"切分：每段非 blank 游程输出一个字符，
        // 与 PaddleOCR 的 (idx != 0 && idx != 前一步) 判据等价，且顺带给出字符的时间步区间。
        int runIndex = -1;
        int runStart = 0;

        for (int t = 0; t <= validSteps; t++)
        {
            int cur = -1;
            if (t < validSteps)
            {
                int off = baseOffset + t * classes;
                float bestVal = float.MinValue;
                cur = 0;
                for (int c = 0; c < classes; c++)
                {
                    float v = logits[off + c];
                    if (v > bestVal) { bestVal = v; cur = c; }
                }
                if (cur != 0)
                {
                    scoreSum += bestVal;
                    scoreCount++;
                }
            }

            if (cur == runIndex) continue;

            // 上一段游程结束于 t（不含），非 blank 则产出一个字符
            if (runIndex > 0)
            {
                string s = runIndex < charset.Length ? charset[runIndex] : "";
                if (s.Length > 0)
                {
                    double u0 = Math.Clamp(runStart * stepWidth / contentWidth, 0, 1);
                    double u1 = Math.Clamp(t * stepWidth / contentWidth, 0, 1);
                    sb.Append(s);
                    chars.Add(new OcrChar(s[0], originX + u0 * spanWidth, originX + u1 * spanWidth));
                }
            }

            runIndex = cur;
            runStart = t;
        }

        double score = scoreCount == 0 ? 0 : scoreSum / scoreCount;
        return (sb.ToString(), score, chars);
    }
}
