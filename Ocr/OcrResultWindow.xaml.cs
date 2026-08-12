using System.Windows;

namespace WeCapture.Ocr;

/// <summary>OCR 结果窗口：只读文本 + 全选复制。</summary>
public partial class OcrResultWindow : Window
{
    public OcrResultWindow(string text)
    {
        InitializeComponent();
        ResultText.Text = string.IsNullOrWhiteSpace(text)
            ? "（未识别到文字，请尝试选择文字更清晰的区域）"
            : text;
        Activated += (_, _) => ResultText.SelectAll();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ResultText.Text);
            CopyButton.Content = "已复制";
        }
        catch
        {
            MessageBox.Show("复制到剪贴板失败", "WeCapture");
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
