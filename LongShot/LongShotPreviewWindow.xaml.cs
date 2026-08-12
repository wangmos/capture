using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WeCapture.Core;
using WeCapture.Export;

namespace WeCapture.LongShot;

/// <summary>
/// 长截图结果预览：默认按"适应宽度"打开——长图动辄几千像素高，
/// 塞进钉图窗只会被整体缩到看不清。滚轮滚动，Ctrl+滚轮以光标为中心缩放。
/// </summary>
public partial class LongShotPreviewWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;

    private readonly BitmapSource _image;
    private readonly AppSettings _settings;
    private double _zoom = 1.0;

    public LongShotPreviewWindow(BitmapSource image, AppSettings settings)
    {
        InitializeComponent();
        _image = image;
        _settings = settings;

        Img.Source = image;
        SizeText.Text = $"{image.PixelWidth} × {image.PixelHeight}";

        // 窗口不超过屏幕的 80%，避免超长图把窗口撑出屏幕
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Math.Max(image.PixelWidth + 90, 520), work.Width * 0.8);
        Height = Math.Min(work.Height * 0.85, 900);

        Loaded += (_, _) => FitWidth();
        PreviewKeyDown += OnKey;
    }

    // ================= 缩放 =================

    private void ApplyZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        Img.Width = _image.PixelWidth * _zoom;
        Img.Height = _image.PixelHeight * _zoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    private void FitWidth()
    {
        double available = Scroller.ActualWidth - 20;   // 留出竖向滚动条
        if (available <= 0 || _image.PixelWidth <= 0) return;
        ApplyZoom(available / _image.PixelWidth);
        Scroller.ScrollToTop();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;   // 无 Ctrl 时交给滚动

        // 以光标位置为锚点缩放：先记下光标处对应的图像坐标，缩放后再滚回去
        var pos = e.GetPosition(Img);
        double imgX = pos.X / _zoom, imgY = pos.Y / _zoom;

        ApplyZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        Scroller.UpdateLayout();

        var inViewport = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(imgX * _zoom - inViewport.X);
        Scroller.ScrollToVerticalOffset(imgY * _zoom - inViewport.Y);
        e.Handled = true;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ApplyZoom(_zoom * 1.25);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ApplyZoom(_zoom / 1.25);

    private void OnFitWidth(object sender, RoutedEventArgs e) => FitWidth();

    private void OnActualSize(object sender, RoutedEventArgs e) => ApplyZoom(1.0);

    // ================= 动作 =================

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!ClipboardHelper.SetImage(_image))
            MessageBox.Show("复制到剪贴板失败（剪贴板可能被其他程序占用）", "WeCapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveHelper.SaveImage(_image, _settings);
        }
        catch (Exception ex)
        {
            TraceLog.Log($"LongShot save failed: {ex}");
            MessageBox.Show($"保存失败：{ex.Message}", "WeCapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.D0 or Key.NumPad0: FitWidth(); break;
            case Key.D1 or Key.NumPad1: ApplyZoom(1.0); break;
            case Key.OemPlus or Key.Add when ctrl: ApplyZoom(_zoom * 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: ApplyZoom(_zoom / 1.25); break;
            case Key.S when ctrl: OnSave(this, new RoutedEventArgs()); break;
            case Key.C when ctrl: OnCopy(this, new RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }
}
