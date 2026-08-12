using System.Windows;
using System.Windows.Input;

namespace WeCapture.Ocr;

/// <summary>OCR 结果窗口：只读文本 + 一键复制。</summary>
public partial class OcrResultWindow : Window
{
    private bool _hasText;

    public OcrResultWindow(string text, string scope = "")
    {
        InitializeComponent();
        SetText(text, scope);

        Activated += (_, _) => ResultText.SelectAll();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>装入（或更新）识别结果。scope 说明这次识别的范围，显示在标题旁。</summary>
    public void SetText(string text, string scope = "")
    {
        _hasText = !string.IsNullOrWhiteSpace(text);
        ResultText.Text = _hasText ? text : "未识别到文字。\n\n可以试试放大后再识别，或换一块对比度更高的区域。";

        if (_hasText)
        {
            int lines = text.Split('\n').Length;
            string stats = $"{text.Replace("\n", "").Length} 字 · {lines} 行";
            StatsText.Text = string.IsNullOrEmpty(scope) ? stats : $"{scope} · {stats}";
        }
        else
        {
            StatsText.Text = scope;
        }

        CopyButton.IsEnabled = _hasText;
        CopyButton.Content = "复制全部";
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!_hasText) return;
        try
        {
            Clipboard.SetText(ResultText.Text);
            CopyButton.Content = "已复制";
        }
        catch
        {
            CopyButton.Content = "复制失败";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
