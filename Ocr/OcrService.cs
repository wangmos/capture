using System.Text;
using System.Windows.Media.Imaging;
using PaddleOCRSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WeCapture.Ocr;

/// <summary>OCR 封装：主路径 PaddleOCR（PP-OCRv5 本地模型，中英文高精度），失败降级 Windows 内置 OCR。</summary>
public static class OcrService
{
    private const int MaxLongEdge = 3000;
    private const int UpscaleBelow = 1500;

    private static readonly Lazy<PaddleOCREngine?> Paddle = new(CreatePaddle);

    public static async void RunAndShow(BitmapSource image)
    {
        try
        {
            string text = await RecognizeAsync(image);
            new OcrResultWindow(text).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"文字识别失败：{ex.Message}", "WeCapture",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    public static async Task<string> RecognizeAsync(BitmapSource image)
    {
        // RenderTargetBitmap 有线程亲和性，像素拷贝必须在 UI 线程完成
        using var bmp = ToBitmap(image);

        try
        {
            var text = await Task.Run(() =>
            {
                var engine = Paddle.Value;
                if (engine == null) return null;
                var r = engine.DetectText(bmp);
                var t = FormatPaddle(r);
                WeCapture.Core.TraceLog.Log($"OCR paddle text: {Short(t)}");
                return t;
            });
            if (!string.IsNullOrWhiteSpace(text)) return text!;
        }
        catch (Exception ex)
        {
            WeCapture.Core.TraceLog.Log($"PaddleOCR failed, fallback to Windows OCR: {ex.Message}");
        }
        return await RecognizeWindowsAsync(image);
    }

    private static string Short(string t)
    {
        t = t.Replace("\r", " ").Replace("\n", " | ");
        return t.Length <= 300 ? t : t.Substring(0, 300) + "...";
    }

    // ================= PaddleOCR 主路径 =================

    private static PaddleOCREngine? CreatePaddle()
    {
        try
        {
            var p = new OCRParameter
            {
                cpu_math_library_num_threads = 4,
                enable_mkldnn = true,
                use_angle_cls = true,
                max_side_len = 3000,
                det_db_box_thresh = 0.4f,
                use_dilation = true,
            };
            return new PaddleOCREngine(null, p);
        }
        catch (Exception ex)
        {
            WeCapture.Core.TraceLog.Log($"PaddleOCR init failed: {ex}");
            return null;
        }
    }

    /// <summary>行按纵向排序；行间距大于 0.6 倍行高视为分段插空行。</summary>
    private static string FormatPaddle(OCRResult r)
    {
        if (r?.TextBlocks == null || r.TextBlocks.Count == 0) return "";

        var sb = new StringBuilder();
        double prevBottom = 0;
        bool first = true;
        foreach (var b in r.TextBlocks.OrderBy(Top).ThenBy(Left))
        {
            double top = Top(b), bottom = Bottom(b);
            if (bottom <= top) continue;
            if (!first && top - prevBottom > (bottom - top) * 0.6)
                sb.AppendLine();
            sb.AppendLine(b.Text);
            prevBottom = bottom;
            first = false;
        }
        return sb.ToString().TrimEnd();
    }

    private static double Top(TextBlock b) => b.BoxPoints.Min(p => p.Y);
    private static double Bottom(TextBlock b) => b.BoxPoints.Max(p => p.Y);
    private static double Left(TextBlock b) => b.BoxPoints.Min(p => p.X);

    private static System.Drawing.Bitmap ToBitmap(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);

        // 深底浅字（暗色主题）整体反色：模型对白底黑字更稳定
        if (AvgLuma(px) < 110)
            for (int i = 0; i + 2 < px.Length; i += 4)
            {
                px[i] = (byte)(255 - px[i]);
                px[i + 1] = (byte)(255 - px[i + 1]);
                px[i + 2] = (byte)(255 - px[i + 2]);
            }

        // 小图放大 2 倍，保证识别模型分辨率（上限 3000）
        double maxSide = Math.Max(w, h);
        if (maxSide < UpscaleBelow)
        {
            double s = Math.Min(2.0, MaxLongEdge / maxSide);
            if (s > 1.0) (px, w, h) = Rescale(px, w, h, s);
        }

        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == w * 4)
            {
                System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(px, y * w * 4,
                        IntPtr.Add(data.Scan0, y * data.Stride), w * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    private static double AvgLuma(byte[] px)
    {
        long sum = 0;
        int n = 0;
        for (int i = 0; i + 2 < px.Length; i += 16)
        {
            sum += (px[i] + (px[i + 1] << 1) + px[i + 2]) >> 2;
            n++;
        }
        return n == 0 ? 255 : sum / (double)n;
    }

    // ================= Windows 内置 OCR 兜底 =================

    private static async Task<string> RecognizeWindowsAsync(BitmapSource image)
    {
        var engines = CreateWindowsEngines();
        using var sb = await Task.Run(() => Prepare(image));

        OcrResult? best = null;
        double bestScore = -1;
        foreach (var engine in engines)
        {
            var r = await engine.RecognizeAsync(sb);
            double score = Score(r);
            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }
        return FormatWindows(best!);
    }

    private static List<OcrEngine> CreateWindowsEngines()
    {
        var result = new List<OcrEngine>();
        var tags = new List<string>();

        var profile = OcrEngine.TryCreateFromUserProfileLanguages();
        if (profile != null)
        {
            result.Add(profile);
            tags.Add(profile.RecognizerLanguage.LanguageTag);
        }

        foreach (var tag in new[] { "zh-Hans-CN", "en-US" })
        {
            if (tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))) continue;
            var e = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
            if (e != null)
            {
                result.Add(e);
                tags.Add(tag);
            }
        }

        if (result.Count == 0)
        {
            var lang = OcrEngine.AvailableRecognizerLanguages
                           .FirstOrDefault(l => l.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                       ?? OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
            if (lang == null)
                throw new InvalidOperationException(
                    "系统未安装任何 OCR 语言包。请在 Windows 设置 → 时间和语言 → 语言 中添加语言（如中文）后重试。");
            var e = OcrEngine.TryCreateFromLanguage(lang);
            if (e == null) throw new InvalidOperationException("无法创建 OCR 引擎。");
            result.Add(e);
        }
        return result;
    }

    private static double Score(OcrResult r)
    {
        int chars = 0, words = 0;
        foreach (var line in r.Lines)
            foreach (var w in line.Words)
            {
                words++;
                foreach (var c in w.Text)
                    if (!char.IsWhiteSpace(c)) chars++;
            }
        return words == 0 ? 0 : chars / (double)words;
    }

    private static string FormatWindows(OcrResult result)
    {
        var sb = new StringBuilder();
        double prevBottom = 0;
        bool first = true;
        foreach (var line in result.Lines)
        {
            double top = double.MaxValue, bottom = 0;
            foreach (var word in line.Words)
            {
                top = Math.Min(top, word.BoundingRect.Y);
                bottom = Math.Max(bottom, word.BoundingRect.Y + word.BoundingRect.Height);
            }
            if (bottom <= top) continue;

            if (!first && top - prevBottom > (bottom - top) * 0.6)
                sb.AppendLine();
            sb.AppendLine(line.Text);
            prevBottom = bottom;
            first = false;
        }
        return sb.ToString().TrimEnd();
    }

    private static SoftwareBitmap Prepare(BitmapSource image)
    {
        var conv = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        int stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);

        GrayStretch(px);

        double maxSide = Math.Max(w, h);
        double scale = maxSide > MaxLongEdge ? MaxLongEdge / maxSide
            : maxSide < UpscaleBelow ? 2.0 : 1.0;
        if (scale > 1.0 && maxSide * scale > MaxLongEdge) scale = MaxLongEdge / maxSide;
        if (Math.Abs(scale - 1.0) > 1e-9)
            (px, w, h) = Rescale(px, w, h, scale);

        var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
        using (var writer = new DataWriter())
        {
            writer.WriteBytes(px);
            sb.CopyFromBuffer(writer.DetachBuffer());
        }
        return sb;
    }

    private static void GrayStretch(byte[] px)
    {
        var hist = new int[256];
        for (int i = 0; i < px.Length; i += 4)
        {
            byte g = (byte)((px[i] + (px[i + 1] << 1) + px[i + 2]) >> 2);
            px[i] = px[i + 1] = px[i + 2] = g;
            hist[g]++;
        }

        int total = px.Length / 4;
        int cut = Math.Max(1, total / 200);
        int lo = 0, hi = 255, acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= cut) { lo = v; break; } }
        acc = 0;
        for (int v = 255; v >= 0; v--) { acc += hist[v]; if (acc >= cut) { hi = v; break; } }
        if (hi - lo < 32) return;

        for (int i = 0; i < px.Length; i += 4)
        {
            int g = Math.Clamp((px[i] - lo) * 255 / (hi - lo), 0, 255);
            px[i] = px[i + 1] = px[i + 2] = (byte)g;
        }
    }

    private static (byte[], int, int) Rescale(byte[] px, int w, int h, double scale)
    {
        int nw = (int)Math.Round(w * scale), nh = (int)Math.Round(h * scale);
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var src = new System.Drawing.Bitmap(w, h, w * 4,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                handle.AddrOfPinnedObject());
            using var dst = new System.Drawing.Bitmap(nw, nh);
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, nw, nh);
            }

            var data = dst.LockBits(new System.Drawing.Rectangle(0, 0, nw, nh),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var raw = new byte[stride * nh];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                if (stride == nw * 4) return (raw, nw, nh);

                var packed = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                    Array.Copy(raw, y * stride, packed, y * nw * 4, nw * 4);
                return (packed, nw, nh);
            }
            finally
            {
                dst.UnlockBits(data);
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
