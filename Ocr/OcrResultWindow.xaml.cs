using System.Windows;
using System.Windows.Input;

namespace WeCapture.Ocr;

/// <summary>OCR 结果窗口：只读文本 + 一键复制。</summary>
public partial class OcrResultWindow : Window
{
    private readonly bool _hasText;

    public OcrResultWindow(string text)
    {
        InitializeComponent();

        _hasText = !string.IsNullOrWhiteSpace(text);
        ResultText.Text = _hasText ? text : "未识别到文字。\n\n可以试试选择更清晰、对比度更高的区域。";

        if (_hasText)
        {
            int lines = text.Split('\n').Length;
            StatsText.Text = $"{text.Replace("\n", "").Length} 字 · {lines} 行";
        }
        else
        {
            CopyButton.IsEnabled = false;
        }

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
