using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeCapture.Annotations;
using WeCapture.Core;
using WeCapture.Export;
using WeCapture.Native;
using WeCapture.Ocr;
using WeCapture.Session;
using WeCapture.Toolbar;

namespace WeCapture.Viewer;

/// <summary>
/// 通用图片查看/编辑窗：截图与长截图共用。
/// 支持缩放、拖动浏览、与截图时一致的标注工具，以及"识别可见区域 / 全文识别"。
/// 标注坐标一律是图片像素，视图层负责按当前缩放换算。
/// </summary>
public partial class ImageViewerWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;

    private readonly BitmapSource _image;
    private readonly AppSettings _settings;
    private readonly ViewerEditor _editor = new();
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private Button? _undoButton;

    private double _zoom = 1.0;
    private BitmapSource? _mosaic;
    private OcrResultWindow? _textWindow;

    // 拖动浏览
    private bool _panning;
    private Point _panStart;
    private double _panOffsetX, _panOffsetY;

    private PointI _textAt;

    public ImageViewerWindow(BitmapSource image, AppSettings settings, string title)
    {
        InitializeComponent();

        Icon = IconFactory.WpfIcon;

        _image = image;
        _settings = settings;
        TitleText.Text = title;
        SizeText.Text = $"{image.PixelWidth} × {image.PixelHeight}";

        Img.Source = image;
        Layer.Attach(_editor, image.PixelWidth, image.PixelHeight, null);
        _editor.Changed += OnEditorChanged;
        _editor.TextRequested += ShowTextEdit;

        BuildToolButtons();
        SizeWindowToImage();

        Loaded += (_, _) =>
        {
            FitWindow();
            Dispatcher.BeginInvoke(new Action(LogRects),
                System.Windows.Threading.DispatcherPriority.Loaded);
        };
        PreviewKeyDown += OnKey;
        Closed += (_, _) =>
        {
            Layer.Detach();
            _textWindow?.Close();
        };
        LocationChanged += (_, _) => PositionTextWindow();
        SizeChanged += (_, _) => PositionTextWindow();
    }

    // ================= 工具条 =================

    private void BuildToolButtons()
    {
        var tools = new (Tool Tool, string Tip, Func<UIElement> Icon)[]
        {
            (Tool.Rectangle, "矩形", () => ToolIcons.Create(Tool.Rectangle)),
            (Tool.Ellipse, "椭圆", () => ToolIcons.Create(Tool.Ellipse)),
            (Tool.Arrow, "箭头", () => ToolIcons.Create(Tool.Arrow)),
            (Tool.Pen, "画笔", () => ToolIcons.Create(Tool.Pen)),
            (Tool.Text, "文字", () => ToolIcons.TextGlyph("A", 14)),
            (Tool.Mosaic, "马赛克", () => ToolIcons.Create(Tool.Mosaic)),
            (Tool.Number, "标号", () => ToolIcons.Number()),
        };

        foreach (var (tool, tip, icon) in tools)
        {
            var b = new ToggleButton
            {
                Style = (Style)FindResource("VwToggle"),
                Content = icon(),
                ToolTip = tip,
            };
            b.Click += (_, _) =>
            {
                // 马赛克需要一张预先块化的整图。必须在选中工具时就备好——
                // 否则画出来的马赛克要等到保存/复制触发生成后才突然显形。
                if (tool == Tool.Mosaic) EnsureMosaic();
                _editor.SetTool(tool);
            };
            _toolButtons[tool] = b;
            ToolsRow.Children.Add(b);
        }

        ToolsRow.Children.Add(new Border
        {
            Width = 1,
            Background = (Brush)FindResource("BorderSubtleBrush"),
            Margin = new Thickness(6, 6, 6, 6),
        });

        _undoButton = new Button
        {
            Style = (Style)FindResource("VwIcon"),
            Content = ToolIcons.Undo(),
            ToolTip = "撤销 (Ctrl+Z)",
            IsEnabled = false,
        };
        _undoButton.Click += (_, _) => _editor.Undo();
        ToolsRow.Children.Add(_undoButton);

        var colors = new[]
        {
            Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0xCC, 0x00),
            Color.FromRgb(0x1E, 0x90, 0xFF), Color.FromRgb(0x2B, 0xD1, 0x2B),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
        };
        foreach (var c in colors)
        {
            var dot = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(c),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
            };
            var cc = c;
            dot.MouseLeftButtonUp += (_, _) => { _editor.DrawColor = cc; OnEditorChanged(); };
            ToolsRow.Children.Add(dot);
        }
    }

    /// <summary>把工具按钮与视口的屏幕坐标写进日志，供 UI 测试定位（同工具条的做法）。</summary>
    private void LogRects()
    {
        try
        {
            var sb = new System.Text.StringBuilder("ViewerRects");
            foreach (var (tool, btn) in _toolButtons)
            {
                var p = btn.PointToScreen(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2));
                sb.Append($" {tool}={(int)p.X},{(int)p.Y}");
            }
            if (_undoButton != null)
            {
                var p = _undoButton.PointToScreen(new Point(_undoButton.ActualWidth / 2, _undoButton.ActualHeight / 2));
                sb.Append($" undo={(int)p.X},{(int)p.Y}");
            }
            foreach (var (name, btn) in new (string, System.Windows.Controls.Button)[]
                     { ("pin", PinButton), ("save", SaveButton), ("copy", CopyButton),
                       ("ocrVisible", OcrVisibleButton), ("ocrAll", OcrAllButton) })
            {
                var q = btn.PointToScreen(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2));
                sb.Append($" {name}={(int)q.X},{(int)q.Y}");
            }

            var vp = Scroller.PointToScreen(new Point(0, 0));
            sb.Append($" viewport={(int)vp.X},{(int)vp.Y},{(int)Scroller.ActualWidth}x{(int)Scroller.ActualHeight}");
            TraceLog.Log(sb.ToString());
        }
        catch
        {
            // 句柄还没建好时忽略
        }
    }

    private void OnEditorChanged()
    {
        foreach (var (tool, btn) in _toolButtons)
            btn.IsChecked = _editor.ActiveTool == tool;
        if (_undoButton != null) _undoButton.IsEnabled = _editor.CanUndo;

        Layer.LastMouse = Layer.LastMouse;
        Layer.InvalidateVisual();
    }

    // ================= 缩放 =================

    private void SizeWindowToImage()
    {
        var work = SystemParameters.WorkArea;
        double maxW = work.Width * 0.9, maxH = work.Height * 0.9;
        const double chromeW = 70, chromeH = 210;

        double fit = Math.Min((maxW - chromeW) / _image.PixelWidth, (maxH - chromeH) / _image.PixelHeight);
        fit = Math.Min(fit, 1.0);

        Width = Math.Clamp(_image.PixelWidth * fit + chromeW, MinWidth, Math.Max(MinWidth, maxW));
        Height = Math.Clamp(_image.PixelHeight * fit + chromeH, MinHeight, Math.Max(MinHeight, maxH));
    }

    private void ApplyZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        double w = _image.PixelWidth * _zoom, h = _image.PixelHeight * _zoom;

        ImageHost.Width = w;
        ImageHost.Height = h;
        Img.Width = w;
        Img.Height = h;
        Layer.Zoom = _zoom;

        RenderOptions.SetBitmapScalingMode(Img,
            _zoom >= 2.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        ZoomText.Text = $"{_zoom * 100:0}%";
        Layer.InvalidateVisual();
    }

    private void FitWindow()
    {
        double w = Scroller.ActualWidth - 4, h = Scroller.ActualHeight - 4;
        if (w <= 0 || h <= 0) return;
        ApplyZoom(Math.Min(w / _image.PixelWidth, h / _image.PixelHeight));
        Scroller.ScrollToTop();
    }

    private void FitWidth()
    {
        double available = Scroller.ActualWidth - 20;
        if (available <= 0) return;
        ApplyZoom(available / _image.PixelWidth);
        Scroller.ScrollToTop();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        var pos = e.GetPosition(ImageHost);
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

    // ================= 鼠标：画标注 / 拖动浏览 =================

    private PointI ToImagePoint(Point p) =>
        new((int)Math.Round(p.X / _zoom), (int)Math.Round(p.Y / _zoom));

    private bool CanPan =>
        Scroller.ScrollableWidth > 0.5 || Scroller.ScrollableHeight > 0.5;

    private void OnImageDown(object sender, MouseButtonEventArgs e)
    {
        CommitTextIfEditing();

        if (_editor.OnDown(ToImagePoint(e.GetPosition(ImageHost))))
        {
            ImageHost.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 没选工具时，左键拖动 = 浏览图片（免去反复拨滚动条去定位局部）
        if (StartPan(e)) e.Handled = true;
    }

    /// <summary>中键拖动始终可平移，不必先取消当前标注工具。</summary>
    private void OnImageMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (StartPan(e)) e.Handled = true;
    }

    private void OnImageMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_panning) return;
        EndPan();
        e.Handled = true;
    }

    private bool StartPan(MouseEventArgs e)
    {
        if (!CanPan) return false;
        _panning = true;
        _panStart = e.GetPosition(Scroller);
        _panOffsetX = Scroller.HorizontalOffset;
        _panOffsetY = Scroller.VerticalOffset;
        ImageHost.CaptureMouse();
        Cursor = Cursors.ScrollAll;
        return true;
    }

    private void EndPan()
    {
        _panning = false;
        ImageHost.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void OnImageMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(_panOffsetX - (now.X - _panStart.X));
            Scroller.ScrollToVerticalOffset(_panOffsetY - (now.Y - _panStart.Y));
            return;
        }

        var p = ToImagePoint(e.GetPosition(ImageHost));
        Layer.LastMouse = p;

        if (_editor.IsDrawing)
        {
            _editor.OnMove(p);
        }
        else
        {
            Cursor = _editor.ActiveTool != Tool.None ? Cursors.Cross
                : CanPan ? Cursors.Hand
                : Cursors.Arrow;
        }
    }

    private void OnImageUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
        {
            EndPan();
            return;
        }

        ImageHost.ReleaseMouseCapture();
        _editor.OnUp(ToImagePoint(e.GetPosition(ImageHost)));
    }

    // ================= 文字标注 =================

    private void ShowTextEdit(PointI at)
    {
        _textAt = at;
        double fontDip = _editor.FontSizePx * _zoom;
        var brush = new SolidColorBrush(_editor.DrawColor);

        TextEdit.Foreground = brush;
        TextEdit.CaretBrush = brush;
        TextEdit.FontSize = Math.Max(8, fontDip);
        TextEdit.Text = "";
        Canvas.SetLeft(TextEdit, at.X * _zoom);
        Canvas.SetTop(TextEdit, at.Y * _zoom - fontDip * 0.12);
        TextEdit.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (TextEdit.Visibility == Visibility.Visible) TextEdit.Focus();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitTextIfEditing()
    {
        if (TextEdit.Visibility != Visibility.Visible) return;
        string text = TextEdit.Text;
        TextEdit.Visibility = Visibility.Hidden;
        _editor.AddText(_textAt, text);
    }

    // ================= 导出（把标注烧进图片） =================

    /// <summary>把当前标注渲染进图片；没有标注时直接返回原图。</summary>
    private BitmapSource Flatten()
    {
        if (_editor.Annotations.Count == 0) return _image;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_image, new Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
            var env = new RenderEnv(new RectI(0, 0, _image.PixelWidth, _image.PixelHeight),
                                    EnsureMosaic(), 1.0);
            foreach (var a in _editor.Annotations)
                a.Render(dc, in env);
        }

        var rtb = new RenderTargetBitmap(_image.PixelWidth, _image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>马赛克需要一张预先块化的整图，按需生成一次。</summary>
    private BitmapSource EnsureMosaic()
    {
        if (_mosaic != null) return _mosaic;

        var conv = new FormatConvertedBitmap(_image, PixelFormats.Bgra32, null, 0);
        var buf = new byte[_image.PixelWidth * 4 * _image.PixelHeight];
        conv.CopyPixels(buf, _image.PixelWidth * 4, 0);
        _mosaic = MosaicImageFactory.CreateFrom(buf, _image.PixelWidth, _image.PixelHeight);

        Layer.SetMosaic(_mosaic);
        return _mosaic;
    }

    // ================= 文字识别 =================

    /// <summary>当前视口对应的图片矩形（图片像素）。</summary>
    private RectI VisibleImageRect()
    {
        double x = Scroller.HorizontalOffset / _zoom;
        double y = Scroller.VerticalOffset / _zoom;
        double w = Math.Min(Scroller.ViewportWidth, ImageHost.ActualWidth) / _zoom;
        double h = Math.Min(Scroller.ViewportHeight, ImageHost.ActualHeight) / _zoom;

        int left = Math.Clamp((int)x, 0, _image.PixelWidth - 1);
        int top = Math.Clamp((int)y, 0, _image.PixelHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(x + w), left + 1, _image.PixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(y + h), top + 1, _image.PixelHeight);
        return RectI.FromLTRB(left, top, right, bottom);
    }

    private void OnOcrVisible(object sender, RoutedEventArgs e)
    {
        var r = VisibleImageRect();
        var crop = new CroppedBitmap(_image, new Int32Rect(r.X, r.Y, r.W, r.H));
        crop.Freeze();
        RunOcr(crop, $"可见区域 {r.W}×{r.H}");
    }

    private void OnOcrAll(object sender, RoutedEventArgs e) => RecognizeAll();

    /// <summary>识别整张图片（截图流程点"识别全部文字"后直接进这里）。</summary>
    public void RecognizeAll() => RunOcr(_image, $"全文 {_image.PixelWidth}×{_image.PixelHeight}");

    private async void RunOcr(BitmapSource source, string scope)
    {
        OcrVisibleButton.IsEnabled = OcrAllButton.IsEnabled = false;
        try
        {
            var snapshot = OcrImage.From(source);
            var result = await Task.Run(() => OcrService.Recognize(snapshot));
            ShowTextWindow(result.ToText(), scope);
        }
        catch (Exception ex)
        {
            TraceLog.Log($"Viewer OCR failed: {ex}");
            MessageBox.Show($"文字识别失败：{ex.Message}", "WeCapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            OcrVisibleButton.IsEnabled = OcrAllButton.IsEnabled = true;
        }
    }

    /// <summary>识别结果窗停靠在图片窗旁边；已经打开则就地更新。</summary>
    public void ShowTextWindow(string text, string scope)
    {
        if (_textWindow is { IsLoaded: true })
        {
            _textWindow.SetText(text, scope);
        }
        else
        {
            // 认作图片窗的附属窗：不占任务栏、随主窗最小化/恢复、始终压在主窗之上
            _textWindow = new OcrResultWindow(text, scope) { Owner = this };
            _textWindow.Closed += (_, _) => _textWindow = null;
            _textWindow.Show();
        }
        PositionTextWindow();
    }

    private void PositionTextWindow()
    {
        if (_textWindow is not { IsLoaded: true } w) return;

        var work = SystemParameters.WorkArea;
        double gap = -10;   // 两窗各有 14px 透明投影边距，负间距看起来才是紧贴的
        double right = Left + Width + gap;

        // 右边放不下就贴到左边
        w.Left = right + w.Width <= work.Right ? right : Math.Max(work.Left, Left - w.Width - gap);
        w.Top = Math.Max(work.Top, Math.Min(Top, work.Bottom - w.Height));
    }

    // ================= 动作 =================

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        CommitTextIfEditing();
        if (!ClipboardHelper.SetImage(Flatten()))
            MessageBox.Show("复制到剪贴板失败（剪贴板可能被其他程序占用）", "WeCapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CommitTextIfEditing();
        try
        {
            SaveHelper.SaveImage(Flatten(), _settings);
        }
        catch (Exception ex)
        {
            TraceLog.Log($"Viewer save failed: {ex}");
            MessageBox.Show($"保存失败：{ex.Message}", "WeCapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        CommitTextIfEditing();
        new Pin.PinWindow(Flatten()).Show();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (TextEdit.Visibility == Visibility.Visible && TextEdit.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter) { CommitTextIfEditing(); e.Handled = true; }
            else if (e.Key == Key.Escape) { TextEdit.Visibility = Visibility.Hidden; e.Handled = true; }
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.D0 or Key.NumPad0: FitWindow(); break;
            case Key.D1 or Key.NumPad1: ApplyZoom(1.0); break;
            case Key.W: FitWidth(); break;
            case Key.OemPlus or Key.Add when ctrl: ApplyZoom(_zoom * 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: ApplyZoom(_zoom / 1.25); break;
            case Key.Z when ctrl: _editor.Undo(); break;
            case Key.S when ctrl: OnSave(this, new RoutedEventArgs()); break;
            case Key.C when ctrl: OnCopy(this, new RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }

    // ================= 边缘缩放 =================

    private void StartResize(int direction)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_SYSCOMMAND,
            new IntPtr(NativeMethods.SC_SIZE + direction), IntPtr.Zero);
    }

    private void OnResizeLeft(object sender, MouseButtonEventArgs e) => StartResize(1);
    private void OnResizeRight(object sender, MouseButtonEventArgs e) => StartResize(2);
    private void OnResizeTop(object sender, MouseButtonEventArgs e) => StartResize(3);
    private void OnResizeBottom(object sender, MouseButtonEventArgs e) => StartResize(6);
    private void OnResizeBottomLeft(object sender, MouseButtonEventArgs e) => StartResize(7);
    private void OnResizeBottomRight(object sender, MouseButtonEventArgs e) => StartResize(8);
}
