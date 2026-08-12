using System.Windows;
using WeCapture.Core;

namespace WeCapture.Ocr;

/// <summary>
/// 图上文字层：把 OCR 结果拍平成"可选字符序列"，坐标为虚拟屏全局物理像素。
/// 选择区间退化为扁平索引上的 [a, b)，跨行选择、复制、高亮都由此派生。
/// </summary>
public sealed class TextLayer
{
    /// <summary>一个可选字符：字形 + 全局矩形 + 所属行。</summary>
    public readonly record struct Item(char Ch, RectI Rect, int LineIndex);

    private readonly List<Item> _items;
    private readonly List<RectI> _lineBoxes;
    private readonly List<int> _lineStart;   // 每行首字符在 _items 中的下标
    private readonly List<int> _lineEnd;     // 每行末字符的下一个下标

    /// <summary>各文本行的外接框（全局 px），用于提示"这里可以选字"。</summary>
    public IReadOnlyList<RectI> LineBoxes => _lineBoxes;

    /// <summary>可选字符总数；光标位置取值范围是 [0, Length]。</summary>
    public int Length => _items.Count;

    public bool IsEmpty => _items.Count == 0;

    /// <summary>整层文字（行间以换行分隔）。</summary>
    public string FullText { get; }

    private TextLayer(List<Item> items, List<RectI> lineBoxes, List<int> lineStart, List<int> lineEnd)
    {
        _items = items;
        _lineBoxes = lineBoxes;
        _lineStart = lineStart;
        _lineEnd = lineEnd;
        FullText = Slice(0, items.Count);
    }

    /// <summary>由识别结果构建；origin 是选区左上角（识别结果为选区局部坐标）。</summary>
    public static TextLayer Build(OcrResult result, PointI origin)
    {
        var items = new List<Item>();
        var boxes = new List<RectI>();
        var starts = new List<int>();
        var ends = new List<int>();

        foreach (var line in result.Lines)
        {
            if (line.Chars.Count == 0 || string.IsNullOrEmpty(line.Text)) continue;

            int lineIndex = boxes.Count;
            starts.Add(items.Count);

            int top = origin.Y + (int)Math.Round(line.Bounds.Top);
            int bottom = origin.Y + (int)Math.Round(line.Bounds.Bottom);

            foreach (var c in line.Chars)
            {
                int left = origin.X + (int)Math.Round(c.Left);
                int right = origin.X + (int)Math.Round(c.Right);
                if (right <= left) right = left + 1;
                items.Add(new Item(c.Ch, RectI.FromLTRB(left, top, right, bottom), lineIndex));
            }

            ends.Add(items.Count);
            boxes.Add(RectI.FromLTRB(
                origin.X + (int)Math.Round(line.Bounds.Left), top,
                origin.X + (int)Math.Round(line.Bounds.Right), bottom));
        }

        return new TextLayer(items, boxes, starts, ends);
    }

    // ================= 命中 =================

    /// <summary>点是否落在某个文本行上（决定是否显示文本光标）。</summary>
    public bool HitsText(PointI p)
    {
        foreach (var b in _lineBoxes)
            if (b.Contains(p)) return true;
        return false;
    }

    /// <summary>
    /// 把坐标换成光标索引 [0, Length]：先选行（不在任何行内时取纵向最近的行），
    /// 行内再按字符中线归到左侧或右侧边界，行为与文本编辑器一致。
    /// </summary>
    public int CaretAt(PointI p)
    {
        if (_items.Count == 0) return 0;

        int lineIndex = -1;
        for (int i = 0; i < _lineBoxes.Count; i++)
        {
            if (p.Y >= _lineBoxes[i].Top && p.Y < _lineBoxes[i].Bottom)
            {
                // 纵向命中多行时（行框重叠），取横向也命中的那一行
                if (lineIndex < 0 || _lineBoxes[i].Contains(p)) lineIndex = i;
            }
        }

        if (lineIndex < 0)
        {
            double best = double.MaxValue;
            for (int i = 0; i < _lineBoxes.Count; i++)
            {
                var b = _lineBoxes[i];
                double d = p.Y < b.Top ? b.Top - p.Y : p.Y >= b.Bottom ? p.Y - b.Bottom + 1 : 0;
                if (d < best) { best = d; lineIndex = i; }
            }
        }

        int start = _lineStart[lineIndex], end = _lineEnd[lineIndex];
        if (p.X < _items[start].Rect.Left) return start;
        if (p.X >= _items[end - 1].Rect.Right) return end;

        for (int i = start; i < end; i++)
        {
            var r = _items[i].Rect;
            if (p.X < r.Right)
                return p.X < r.X + r.W / 2 ? i : i + 1;
        }
        return end;
    }

    // ================= 取词 / 取行 =================

    /// <summary>双击取词：中日韩字符取单字，拉丁与数字取连续串。</summary>
    public (int Start, int End) WordAt(int caret)
    {
        if (_items.Count == 0) return (0, 0);
        int i = Math.Clamp(caret, 0, _items.Count - 1);
        int line = _items[i].LineIndex;
        char c = _items[i].Ch;

        if (!IsWordChar(c)) return (i, i + 1);

        int start = i, end = i + 1;
        while (start - 1 >= _lineStart[line] && IsWordChar(_items[start - 1].Ch)) start--;
        while (end < _lineEnd[line] && IsWordChar(_items[end].Ch)) end++;
        return (start, end);
    }

    /// <summary>三击取整行。</summary>
    public (int Start, int End) LineAt(int caret)
    {
        if (_items.Count == 0) return (0, 0);
        int i = Math.Clamp(caret, 0, _items.Count - 1);
        int line = _items[i].LineIndex;
        return (_lineStart[line], _lineEnd[line]);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) && !IsCjk(c) || c == '_' || c == '-' || c == '.';

    private static bool IsCjk(char c) =>
        c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿'
             or >= '぀' and <= 'ヿ' or >= '가' and <= '힯';

    // ================= 取文 / 高亮 =================

    /// <summary>取出 [a, b) 的文字，跨行处插入换行。</summary>
    public string Slice(int a, int b)
    {
        a = Math.Clamp(a, 0, _items.Count);
        b = Math.Clamp(b, 0, _items.Count);
        if (b <= a) return "";

        var sb = new System.Text.StringBuilder(b - a);
        int prevLine = _items[a].LineIndex;
        for (int i = a; i < b; i++)
        {
            if (_items[i].LineIndex != prevLine)
            {
                sb.Append('\n');
                prevLine = _items[i].LineIndex;
            }
            sb.Append(_items[i].Ch);
        }
        return sb.ToString();
    }

    /// <summary>[a, b) 的高亮矩形：同一行内的字符合并成一块。</summary>
    public List<RectI> HighlightRects(int a, int b)
    {
        var rects = new List<RectI>();
        a = Math.Clamp(a, 0, _items.Count);
        b = Math.Clamp(b, 0, _items.Count);
        if (b <= a) return rects;

        int runLine = _items[a].LineIndex;
        var acc = _items[a].Rect;

        for (int i = a + 1; i < b; i++)
        {
            if (_items[i].LineIndex == runLine)
            {
                acc = acc.Union(_items[i].Rect);
            }
            else
            {
                rects.Add(acc);
                runLine = _items[i].LineIndex;
                acc = _items[i].Rect;
            }
        }
        rects.Add(acc);
        return rects;
    }
}
