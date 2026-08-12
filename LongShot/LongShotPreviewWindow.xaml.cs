using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

        SizeWindowToImage();
        Loaded += (_, _) => FitWindow();
        PreviewKeyDown += OnKey;
    }

    /// <summary>
    /// 按图片长宽比给窗口定尺寸（限制在工作区内），让整图能以自然比例铺满视口，
    /// 而不是开出一个大窗配一张小图。
    /// </summary>
    private void SizeWindowToImage()
    {
        var work = SystemParameters.WorkArea;
        double maxW = work.Width * 0.85, maxH = work.Height * 0.9;

        // 视口之外的固定开销：窗口外边距/标题栏/底部操作栏
        const double chromeW = 60, chromeH = 150;

        double fit = Math.Min((maxW - chromeW) / _image.PixelWidth,
                              (maxH - chromeH) / _image.PixelHeight);
        fit = Math.Min(fit, 1.0);

        // 下限不是随意取的：底部那排缩放/操作按钮排下来约需 660px，窄于此就会互相压盖
        Width = Math.Clamp(_image.PixelWidth * fit + chromeW, 660, Math.Max(660, maxW));
        Height = Math.Clamp(_image.PixelHeight * fit + chromeH, 420, maxH);
    }

    // ================= 缩放 =================

    private void ApplyZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        Img.Width = _image.PixelWidth * _zoom;
        Img.Height = _image.PixelHeight * _zoom;

        // 放大看细节时用最近邻，保持像素锐利；缩小时用高质量重采样避免摩尔纹
        RenderOptions.SetBitmapScalingMode(Img,
            _zoom >= 2.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    /// <summary>整图适应窗口（默认视图）。</summary>
    private void FitWindow()
    {
        double w = Scroller.ActualWidth - 4, h = Scroller.ActualHeight - 4;
        if (w <= 0 || h <= 0 || _image.PixelWidth <= 0 || _image.PixelHeight <= 0) return;
        ApplyZoom(Math.Min(w / _image.PixelWidth, h / _image.PixelHeight));
        Scroller.ScrollToTop();
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

    private void OnFitWindow(object sender, RoutedEventArgs e) => FitWindow();

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
            case Key.D0 or Key.NumPad0: FitWindow(); break;
            case Key.W: FitWidth(); break;
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
