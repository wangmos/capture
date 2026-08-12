namespace WeCapture.LongShot;

/// <summary>一帧的拼接结果。</summary>
public enum StitchStatus
{
    /// <summary>成功追加新内容。</summary>
    Appended,

    /// <summary>画面没动（到底了，或这一步滚动没生效）。</summary>
    NoMovement,

    /// <summary>找不到可信的重叠位置——滚太快导致两帧没有公共内容，必须回退重来。</summary>
    LowConfidence,
}

public readonly record struct StitchStep(StitchStatus Status, int Delta, double Score);

/// <summary>
/// 滚动截屏拼接器：逐帧求纵向位移并追加新内容。
///
/// 定位方式是两级的——先用"行签名"（每行压成 K 个亮度桶）在候选位移上做序列匹配，
/// 代价是 O(候选数 × 带宽 × K)；选出最佳位移后再用真实像素复核，得到一个可判定的
/// 匹配分数。分数不达标时返回 LowConfidence，由调用方回退步长重试，而不是硬拼——
/// 静默拼错正是旧版长截图不可用的根源。
///
/// 固定表头/表尾（跨帧不动的区带）会被识别并排除，避免重复拼接。
/// </summary>
public sealed class ScrollStitcher
{
    private const int Buckets = 32;          // 每行压缩成多少个亮度桶
    private const int MinOverlapRows = 24;   // 判定可信所需的最小重叠行数
    private const double MaxHeaderFraction = 0.4;

    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;

    /// <summary>已拼接的内容（BGRA，行数 = <see cref="TotalHeight"/>）。</summary>
    private readonly List<byte[]> _rows = new();

    private float[]? _prevSig;   // 上一帧的行签名，[row * Buckets + b]
    private byte[]? _prevFrame;
    private int _framesSeen = 1;

    public int Width => _width;
    public int TotalHeight => _rows.Count;

    /// <summary>识别出的固定表头/表尾行数（拼接时已排除）。</summary>
    public int HeaderRows { get; private set; }
    public int FooterRows { get; private set; }

    /// <summary>匹配分数低于此值视为不可信（0~1，越大越严格）。</summary>
    public double ScoreThreshold { get; init; } = 0.90;

    public ScrollStitcher(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("帧尺寸非法");
        _width = width;
        _height = height;
        _stride = width * 4;
    }

    /// <summary>加入一帧。首帧全量收下，其后按位移追加。</summary>
    public StitchStep AddFrame(byte[] frame)
    {
        if (frame.Length < _stride * _height)
            throw new ArgumentException("帧数据长度不足", nameof(frame));

        var sig = BuildSignatures(frame);

        if (_prevSig == null || _prevFrame == null)
        {
            for (int y = 0; y < _height; y++)
                _rows.Add(CopyRow(frame, y));
            _prevSig = sig;
            _prevFrame = (byte[])frame.Clone();
            return new StitchStep(StitchStatus.Appended, _height, 1.0);
        }

        // 1) 跨帧不动的首尾区带 = 固定表头/表尾
        int header = LeadingIdenticalRows(_prevSig, sig);
        int footer = TrailingIdenticalRows(_prevSig, sig, header);

        if (header >= _height)
            return new StitchStep(StitchStatus.NoMovement, 0, 1.0);   // 整帧未变

        // 取跨帧的最小值：表头下方若恰好是空白行，单帧会把它们误算进表头，
        // 多帧取最小可以收敛到真正固定不动的区带（这两个值仅用于展示/排障）。
        HeaderRows = _framesSeen == 1 ? header : Math.Min(HeaderRows, header);
        FooterRows = _framesSeen == 1 ? footer : Math.Min(FooterRows, footer);
        _framesSeen++;

        // 2) 在可动区带内搜索位移
        int top = header;
        int bottom = _height - footer;
        int span = bottom - top;
        if (span < MinOverlapRows)
            return new StitchStep(StitchStatus.LowConfidence, 0, 0);

        var (delta, score) = FindDelta(_prevSig, sig, top, bottom);

        if (delta <= 0)
        {
            _prevSig = sig;
            _prevFrame = (byte[])frame.Clone();
            return new StitchStep(StitchStatus.NoMovement, 0, score);
        }

        if (score < ScoreThreshold)
            return new StitchStep(StitchStatus.LowConfidence, delta, score);

        // 3) 追加本帧新露出的行（位于可动区带底部）
        for (int y = bottom - delta; y < bottom; y++)
            _rows.Add(CopyRow(frame, y));

        _prevSig = sig;
        _prevFrame = (byte[])frame.Clone();
        return new StitchStep(StitchStatus.Appended, delta, score);
    }

    /// <summary>导出拼接结果（BGRA，紧凑行距）。</summary>
    public byte[] ToBgra()
    {
        var result = new byte[_stride * _rows.Count];
        for (int y = 0; y < _rows.Count; y++)
            Buffer.BlockCopy(_rows[y], 0, result, y * _stride, _stride);
        return result;
    }

    // ================= 位移搜索 =================

    /// <summary>
    /// 找使 new[top..top+band) 与 prev[top+d..top+d+band) 最匹配的 d。
    /// 匹配带随位移自适应收缩（位移越大、可用重叠越少），否则可检测的最大位移会被
    /// 固定带宽卡死——滚动步长稍大就会误判为"找不到重叠"。
    /// 分数同时考虑绝对代价和峰值锐度：一片空白区域里处处都"匹配"，必须判为不可信。
    /// </summary>
    private (int Delta, double Score) FindDelta(float[] prevSig, float[] sig, int top, int bottom)
    {
        const int MaxBand = 120;
        int span = bottom - top;

        double best = double.MaxValue;
        int bestDelta = 0;
        double sum = 0;
        int n = 0;

        for (int d = 1; d <= span - MinOverlapRows; d++)
        {
            int band = Math.Min(MaxBand, span - d);
            if (band < MinOverlapRows) break;

            double cost = 0;
            for (int r = 0; r < band; r++)
            {
                int a = (top + r) * Buckets;
                int b = (top + d + r) * Buckets;
                for (int k = 0; k < Buckets; k++)
                {
                    float diff = sig[a + k] - prevSig[b + k];
                    cost += diff < 0 ? -diff : diff;
                }
            }
            cost /= band * Buckets;

            sum += cost;
            n++;

            if (cost < best)
            {
                best = cost;
                bestDelta = d;
            }
        }

        if (bestDelta == 0 || n == 0) return (0, 0);

        double mean = sum / n;
        // 绝对匹配度：代价 0 → 1 分；代价 8（灰度级）以上基本判负
        double fit = Math.Max(0, 1.0 - best / 8.0);
        // 峰值锐度：最佳明显优于平均才算真正对上，全平区域 mean≈best → 0 分
        double sharp = mean <= 1e-6 ? 0 : Math.Clamp((mean - best) / mean, 0, 1);

        return (bestDelta, fit * 0.5 + sharp * 0.5);
    }

    // ================= 行签名 =================

    /// <summary>把每行压成 Buckets 个亮度均值，作为快速匹配的特征。</summary>
    private float[] BuildSignatures(byte[] frame)
    {
        var sig = new float[_height * Buckets];

        for (int y = 0; y < _height; y++)
        {
            int rowOff = y * _stride;
            int sigOff = y * Buckets;
            for (int b = 0; b < Buckets; b++)
            {
                int x0 = b * _width / Buckets;
                int x1 = (b + 1) * _width / Buckets;
                if (x1 <= x0) x1 = x0 + 1;
                if (x1 > _width) x1 = _width;

                int sum = 0, count = 0;
                for (int x = x0; x < x1; x++)
                {
                    int i = rowOff + x * 4;
                    sum += (frame[i] + (frame[i + 1] << 1) + frame[i + 2]) >> 2;  // 近似亮度
                    count++;
                }
                sig[sigOff + b] = count == 0 ? 0 : (float)sum / count;
            }
        }
        return sig;
    }

    private static bool RowsEqual(float[] a, float[] b, int row, float tolerance = 1.5f)
    {
        int off = row * Buckets;
        for (int k = 0; k < Buckets; k++)
        {
            float d = a[off + k] - b[off + k];
            if (d < 0) d = -d;
            if (d > tolerance) return false;
        }
        return true;
    }

    private int LeadingIdenticalRows(float[] a, float[] b)
    {
        int limit = (int)(_height * MaxHeaderFraction);
        int y = 0;
        while (y < _height && RowsEqual(a, b, y)) y++;
        return y >= _height ? _height : Math.Min(y, limit);
    }

    private int TrailingIdenticalRows(float[] a, float[] b, int header)
    {
        int limit = (int)(_height * MaxHeaderFraction);
        int count = 0;
        for (int y = _height - 1; y > header && count < limit; y--)
        {
            if (!RowsEqual(a, b, y)) break;
            count++;
        }
        return count;
    }

    private byte[] CopyRow(byte[] frame, int y)
    {
        var row = new byte[_stride];
        Buffer.BlockCopy(frame, y * _stride, row, 0, _stride);
        return row;
    }
}
