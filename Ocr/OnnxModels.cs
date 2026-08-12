using System.IO;
using Microsoft.ML.OnnxRuntime;
using WeCapture.Core;

namespace WeCapture.Ocr;

/// <summary>
/// PP-OCRv6 small 的 ONNX Runtime 会话管理（det / rec 各一，懒加载、进程内复用）。
/// 模型随程序分发在 Models\ 目录；字典优先从 rec 模型的 metadata 读取，保证与权重严格同源。
/// </summary>
internal static class OnnxModels
{
    public const string DetFileName = "PP-OCRv6_det_small.onnx";
    public const string RecFileName = "PP-OCRv6_rec_small.onnx";
    public const string DictFileName = "ppocrv6_dict.txt";

    private static readonly Lazy<InferenceSession> DetSession = new(() => Create(DetFileName));
    private static readonly Lazy<InferenceSession> RecSession = new(() => Create(RecFileName));
    private static readonly Lazy<string[]> Charset = new(LoadCharset);

    public static InferenceSession Det => DetSession.Value;
    public static InferenceSession Rec => RecSession.Value;

    /// <summary>CTC 字符表：索引 0 为 blank，末位为空格（与模型输出类别数一致）。</summary>
    public static string[] Characters => Charset.Value;

    public static string ModelDir => Path.Combine(AppContext.BaseDirectory, "Models");

    private static InferenceSession Create(string fileName)
    {
        string path = Path.Combine(ModelDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"缺少 OCR 模型文件：{path}", path);

        var opts = new SessionOptions
        {
            // 截图区域通常不大，线程开太多反而被调度开销吃掉
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var session = new InferenceSession(path, opts);
        TraceLog.Log($"OCR model loaded {fileName} in {sw.ElapsedMilliseconds}ms");
        return session;
    }

    /// <summary>
    /// 组装 CTC 字符表：blank + 字典 + 空格。
    /// 字典来源优先级：rec 模型内嵌 metadata["character"] &gt; Models\ppocrv6_dict.txt。
    /// </summary>
    private static string[] LoadCharset()
    {
        string[]? dict = null;

        try
        {
            if (Rec.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var embedded) &&
                !string.IsNullOrEmpty(embedded))
            {
                dict = embedded.Split('\n');
                // 末尾换行会带出一个空项，但字典中间的空项是有效字符，只能裁掉尾部
                if (dict.Length > 0 && dict[^1].Length == 0)
                    dict = dict[..^1];
                for (int i = 0; i < dict.Length; i++)
                    dict[i] = dict[i].TrimEnd('\r');
                TraceLog.Log($"OCR charset from model metadata: {dict.Length}");
            }
        }
        catch (Exception ex)
        {
            TraceLog.Log($"OCR charset metadata read failed: {ex.Message}");
        }

        if (dict == null)
        {
            string path = Path.Combine(ModelDir, DictFileName);
            dict = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            TraceLog.Log($"OCR charset from file: {dict.Length}");
        }

        // PaddleOCR 约定：0 号为 CTC blank，use_space_char 时末尾追加空格
        var chars = new string[dict.Length + 2];
        chars[0] = "";
        Array.Copy(dict, 0, chars, 1, dict.Length);
        chars[^1] = " ";
        return chars;
    }

    /// <summary>提前在后台加载模型，避免首次识别时的秒级停顿。</summary>
    public static void Warmup()
    {
        try
        {
            _ = Det;
            _ = Rec;
            _ = Characters;
        }
        catch (Exception ex)
        {
            TraceLog.Log($"OCR warmup failed: {ex.Message}");
        }
    }
}
