using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WeCapture.Pin;

/// <summary>钉住窗口：无边框置顶，可拖动，悬停显示关闭按钮，可多开。</summary>
public partial class PinWindow : Window
{
    private static int _openCount;

    public PinWindow(BitmapSource image)
    {
        InitializeComponent();
        Img.Source = image;

        // 限制尺寸：不超过光标所在屏工作区的 80%，超出则等比缩小
        Loaded += (_, _) => ClampSize(image);
        Closed += (_, _) => _openCount--;
        _openCount++;
    }

    private void ClampSize(BitmapSource image)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        double maxW = screen.WorkingArea.Width * 0.8;
        double maxH = screen.WorkingArea.Height * 0.8;

        // 96dpi 导出图：DIP 尺寸 == 像素尺寸
        double w = image.Width;
        double h = image.Height;

        if (w > maxW || h > maxH)
        {
            double ratio = Math.Min(maxW / w, maxH / h);
            Img.Stretch = System.Windows.Media.Stretch.Uniform;
            Img.Width = w * ratio;
            Img.Height = h * ratio;
        }

        // 初始位置：鼠标附近，限制在屏幕内
        var mp = System.Windows.Forms.Control.MousePosition;
        double left = Math.Clamp(mp.X - ActualWidth / 2, screen.WorkingArea.Left, screen.WorkingArea.Right - ActualWidth);
        double top = Math.Clamp(mp.Y - 20, screen.WorkingArea.Top, screen.WorkingArea.Bottom - ActualHeight);
        Left = left;
        Top = top;
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
