using System.Windows;
using System.Windows.Media.Imaging;

namespace WeCapture.Ocr;

/// <summary>
/// 送入 OCR 的位图快照（BGRA32，紧凑行距）。
/// BitmapSource 有线程亲和性，像素必须在 UI 线程取出后再交给后台管线。
/// </summary>
public sealed class OcrImage
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public int Stride => Width * 4;

    public static OcrImage From(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var buf = new byte[w * h * 4];
        conv.CopyPixels(buf, w * 4, 0);
        return new OcrImage { Bgra = buf, Width = w, Height = h };
    }
}

/// <summary>识别出的单个字符及其在图像中的横向区间（图像局部像素）。</summary>
public readonly record struct OcrChar(char Ch, double Left, double Right);

/// <summary>一行文本：检测框 + 识别结果 + 逐字符横向位置（支持鼠标划选）。</summary>
public sealed class OcrLine
{
    /// <summary>检测得到的四点框（顺时针，图像局部像素）。</summary>
    public required Point[] Quad { get; init; }

    /// <summary>四点框的轴对齐外接矩形。</summary>
    public required Rect Bounds { get; init; }

    /// <summary>检测置信度（概率图均值）。</summary>
    public required double DetScore { get; init; }

    public string Text { get; set; } = "";

    /// <summary>识别置信度（各时间步最大概率均值）。</summary>
    public double RecScore { get; set; }

    public IReadOnlyList<OcrChar> Chars { get; set; } = Array.Empty<OcrChar>();

    public override string ToString() => $"[{Bounds.X:0},{Bounds.Y:0} {Bounds.Width:0}x{Bounds.Height:0}] {Text}";
}

/// <summary>一次识别的完整结果。</summary>
public sealed class OcrResult
{
    public required IReadOnlyList<OcrLine> Lines { get; init; }

    /// <summary>识别耗时（毫秒），排障用。</summary>
    public long ElapsedMs { get; init; }

    public static OcrResult Empty { get; } = new() { Lines = Array.Empty<OcrLine>() };

    /// <summary>按阅读顺序拼接为纯文本；行间距明显变大处插入空行分段。</summary>
    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        double prevBottom = 0;
        bool first = true;
        foreach (var line in Lines)
        {
            if (string.IsNullOrEmpty(line.Text)) continue;
            double top = line.Bounds.Top, bottom = line.Bounds.Bottom;
            if (!first && top - prevBottom > (bottom - top) * 0.6)
                sb.AppendLine();
            sb.AppendLine(line.Text);
            prevBottom = bottom;
            first = false;
        }
        return sb.ToString().TrimEnd();
    }
}
