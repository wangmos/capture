using System.Diagnostics;
using System.Windows.Media.Imaging;
using WeCapture.Core;

namespace WeCapture.Ocr;

/// <summary>
/// OCR 编排：PP-OCRv6 small（det + rec）跑在 ONNX Runtime 上。
/// 两条路径——整图识别走 det+rec；固定区域（单行）跳过检测直接走 rec。
/// </summary>
public static class OcrService
{
    /// <summary>暗底浅字整体反色的亮度阈值。</summary>
    private const double DarkLumaThreshold = 110;

    /// <summary>
    /// 通道顺序：PaddleOCR 的 det/rec inference.yml 均为 img_mode: BGR，
    /// 即网络按 B,G,R 顺序接收并套用 ImageNet 常数。留作开关以便实测比对。
    /// </summary>
    internal static bool SwapToRgb { get; set; }

    /// <summary>模型预热（后台线程调用，避免首次识别的秒级停顿）。</summary>
    public static void Warmup() => Task.Run(OnnxModels.Warmup);

    // ================= 对外入口 =================

    /// <summary>工具条 OCR 按钮：识别整块选区并弹出结果窗口。</summary>
    public static async void RunAndShow(BitmapSource image)
    {
        try
        {
            var snapshot = OcrImage.From(image);
            var result = await Task.Run(() => Recognize(snapshot));
            new OcrResultWindow(result.ToText()).Show();
        }
        catch (Exception ex)
        {
            TraceLog.Log($"OCR failed: {ex}");
            MessageBox.Show($"文字识别失败：{ex.Message}", "WeCapture",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>识别整块图像：检测 + 识别，返回带字符位置的完整结果。</summary>
    public static OcrResult Recognize(OcrImage image)
    {
        var sw = Stopwatch.StartNew();
        var img = Preprocess(image);

        // 选区本身就是一行文字时，检测只会绕远路：直接走 rec 更快也更准
        if (LooksSingleLine(img.Width, img.Height))
        {
            var (text, score, chars) = TextRecognizer.RecognizeSingle(img, SwapToRgb);
            var line = new OcrLine
            {
                Quad = new[]
                {
                    new System.Windows.Point(0, 0), new System.Windows.Point(img.Width, 0),
                    new System.Windows.Point(img.Width, img.Height), new System.Windows.Point(0, img.Height),
                },
                Bounds = new System.Windows.Rect(0, 0, img.Width, img.Height),
                DetScore = 1.0,
                Text = text,
                RecScore = score,
                Chars = chars,
            };
            TraceLog.Log($"OCR single-line {img.Width}x{img.Height} in {sw.ElapsedMilliseconds}ms: {Short(text)}");
            return new OcrResult { Lines = new[] { line }, ElapsedMs = sw.ElapsedMilliseconds };
        }

        var lines = TextDetector.Detect(img, SwapToRgb);
        TextRecognizer.RecognizeLines(img, lines, SwapToRgb);

        // 识别为空的框丢掉（多为图标、边框误检）
        var kept = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
        var result = new OcrResult { Lines = kept, ElapsedMs = sw.ElapsedMilliseconds };
        TraceLog.Log($"OCR done lines={kept.Count}/{lines.Count} in {sw.ElapsedMilliseconds}ms: {Short(result.ToText())}");
        return result;
    }

    /// <summary>固定区域识别：不跑检测，整块当作一行送 rec。</summary>
    public static string RecognizeFixedRegion(OcrImage image)
    {
        var img = Preprocess(image);
        var (text, _, _) = TextRecognizer.RecognizeSingle(img, SwapToRgb);
        return text;
    }

    // ================= 预处理 =================

    private static OcrImage Preprocess(OcrImage image) =>
        ImageOps.AverageLuma(image) < DarkLumaThreshold ? ImageOps.Invert(image) : image;

    /// <summary>形似单行文本：高度不大且明显扁长。</summary>
    internal static bool LooksSingleLine(int w, int h) =>
        h > 0 && h <= 64 && w >= h * 4;

    private static string Short(string t)
    {
        t = t.Replace("\r", " ").Replace("\n", " | ");
        return t.Length <= 300 ? t : t[..300] + "...";
    }
}
