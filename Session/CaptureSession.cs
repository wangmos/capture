using System.Windows.Media.Imaging;
using WeCapture.Annotations;
using WeCapture.Capture;
using WeCapture.Core;
using WeCapture.Export;
using WeCapture.Overlay;
using WeCapture.Pin;
using WeCapture.Session;
using WeCapture.Toolbar;

namespace WeCapture.Session;

/// <summary>
/// 一次截图会话的生命周期：截屏 → 覆盖层 → 交互 → 导出。
/// 全局单例（Active 非空表示正在截图）。
/// </summary>
public sealed class CaptureSession
{
    private static CaptureSession? _active;
    public static bool IsActive => _active != null;

    private readonly AppSettings _settings;
    private readonly MonitorSet _monitors;
    private readonly SessionModel _model;
    private readonly HoverDetector _hoverDetector = new();
    private readonly List<OverlayWindow> _windows = new();
    private BitmapSource? _mosaicCache;
    private RectI _mosaicCacheSel;
    private bool _closing;
    private bool _longShotRunning;

    public SessionModel Model => _model;
    public MonitorSet Monitors => _monitors;

    public static void Start(AppSettings settings)
    {
        if (_active != null) return;
        try
        {
            _active = new CaptureSession(settings);
        }
        catch (Exception ex)
        {
            _active = null;
            Core.TraceLog.Log($"CaptureSession.Start FAILED: {ex}");
            MessageBox.Show($"启动截图失败：{ex.Message}", "WeCapture",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private CaptureSession(AppSettings settings)
    {
        Core.TraceLog.Log("CaptureSession ctor begin");
        _settings = settings;
        _monitors = MonitorSet.CaptureAll();
        _model = new SessionModel(_monitors.VirtualBounds) { AutoTextSelect = settings.AutoTextSelect };

        _model.CopyConfirmed += DoCopyAndExit;
        _model.ExitRequested += ExitAll;
        _model.TextEditRequested += OnTextEditRequested;
        _model.TextLayerRequested += OnTextLayerRequested;
        _model.TextCopyRequested += OnTextCopyRequested;

        // 快捷键与工具条按钮走同一套动作
        _model.SaveRequested += DoSaveAndExit;
        _model.PinRequested += DoPinAndExit;
        _model.OcrRequested += DoOcr;
        _model.LongShotRequested += DoLongShot;

        CreateWindows();

        // 覆盖层已显示，此时在后台加载 OCR 模型：用户真去点识别时就不必等秒级的首次加载
        Ocr.OcrService.Warmup();

        Core.TraceLog.Log($"CaptureSession ctor done, windows={_windows.Count}");
    }

    private void CreateWindows()
    {
        foreach (var mon in _monitors.Monitors)
            _windows.Add(new OverlayWindow(this, mon, _hoverDetector));

        foreach (var w in _windows)
        {
            w.PlaceExactly();
            _hoverDetector.AddExcludeHwnd(w.Hwnd);
            WireToolbar(w.Toolbar);
        }

        var primary = _windows.FirstOrDefault(w => w.Monitor.IsPrimary) ?? _windows[0];
        primary.FocusOverlay();
    }

    private void WireToolbar(ToolbarControl tb)
    {
        tb.ToolSelected += _model.SetTool;
        tb.UndoClicked += _model.Undo;
        tb.OcrClicked += DoOcr;
        tb.LongShotClicked += DoLongShot;
        tb.PinClicked += DoPinAndExit;
        tb.SaveClicked += DoSaveAndExit;
        tb.CopyClicked += DoCopyAndExit;
        tb.ExitClicked += ExitAll;
        tb.ColorSelected += _model.SetDrawColor;
        tb.ThicknessSelected += _model.SetThickness;
        tb.FontSizeSelected += _model.SetFontSize;
        tb.MosaicRadiusSelected += _model.SetMosaicRadius;
    }

    private void OnTextEditRequested(PointI pos)
    {
        var win = _windows.FirstOrDefault(w => w.Monitor.BoundsPx.Contains(pos)) ?? _windows[0];
        win.ShowTextEdit(pos);
    }

    // ================= 图上选字 =================

    /// <summary>进入取字模式：对选区原图（不含标注）跑一次 OCR，完成后装入模型。</summary>
    private void OnTextLayerRequested()
    {
        if (_model.Selection is not RectI sel || sel.IsEmpty) return;

        // RenderTargetBitmap / BitmapSource 有线程亲和性，像素必须在 UI 线程取出
        var raw = ImageExporter.Render(_monitors, sel, Array.Empty<Annotation>(), null);
        var snapshot = Ocr.OcrImage.From(raw);
        var origin = sel.Location;
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        Task.Run(() =>
        {
            try
            {
                var result = Ocr.OcrService.Recognize(snapshot);
                var layer = Ocr.TextLayer.Build(result, origin);
                dispatcher.BeginInvoke(() =>
                {
                    if (!_closing) _model.SetTextLayer(layer, sel);
                });
            }
            catch (Exception ex)
            {
                Core.TraceLog.Log($"TextLayer build failed: {ex}");
                dispatcher.BeginInvoke(() =>
                {
                    if (!_closing) _model.SetTextLayerFailed();
                });
            }
        });
    }

    /// <summary>复制选中的文字并结束会话。</summary>
    private void OnTextCopyRequested(string text)
    {
        ExitAll();
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Core.TraceLog.Log($"Clipboard.SetText failed: {ex.Message}");
            MessageBox.Show("复制到剪贴板失败（剪贴板可能被其他程序占用）", "WeCapture",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    // ================= 导出 =================

    /// <summary>按当前选区 + 标注渲染最终图（物理像素）。</summary>
    public BitmapSource RenderSelection()
    {
        if (_model.Selection is not RectI sel || sel.IsEmpty)
            throw new InvalidOperationException("当前无有效选区");
        return ImageExporter.Render(_monitors, sel, _model.Annotations, GetMosaicImage());
    }

    /// <summary>马赛克源图：懒加载并按选区缓存（选区变化时重建）。</summary>
    public BitmapSource? GetMosaicImage()
    {
        bool needed = _model.Annotations.Any(a => a is MosaicAnnotation)
            || (_model.DragMode == DragMode.Draw && _model.ActiveTool == Tool.Mosaic);
        if (!needed) return null;
        if (_model.Selection is not RectI sel || sel.IsEmpty) return null;

        if (_mosaicCache == null || _mosaicCacheSel != sel)
        {
            _mosaicCache = MosaicImageFactory.Create(_monitors, sel);
            _mosaicCacheSel = sel;
        }
        return _mosaicCache;
    }

    // ================= 动作 =================

    private void DoCopyAndExit()
    {
        // 取字模式下选中了文字：复制按钮/双击复制的是文字而不是图片
        if (_model.ActiveTool == Tool.TextSelect && _model.HasTextSelection)
        {
            _model.CopyTextSelection();
            return;
        }

        if (_model.Selection == null) return;
        var img = RenderSelection();
        ExitAll();
        if (!ClipboardHelper.SetImage(img))
            MessageBox.Show("复制到剪贴板失败（剪贴板可能被其他程序占用）", "WeCapture",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    private void DoSaveAndExit()
    {
        if (_model.Selection == null) return;
        var img = RenderSelection();
        if (SaveHelper.SaveImage(img, _settings))
            ExitAll();
    }

    private void DoPinAndExit()
    {
        if (_model.Selection == null) return;
        var img = RenderSelection();
        ExitAll();
        new PinWindow(img).Show();
    }

    private void DoOcr()
    {
        if (_model.Selection == null) return;
        var img = RenderSelection();
        ExitAll();
        Ocr.OcrService.RunAndShow(img);
    }

    // ================= 长截图 =================

    /// <summary>
    /// 长截图：隐藏覆盖层 → 应用自己驱动滚动并逐帧拼接 → 结果复制到剪贴板并钉出来。
    /// 覆盖层必须先隐藏，因为拼接看的是滚动中的实时画面，不是会话开始时的冻结截图。
    /// </summary>
    private async void DoLongShot()
    {
        if (_model.Selection is not RectI sel || sel.IsEmpty) return;
        if (_longShotRunning) return;
        _longShotRunning = true;

        var cts = new CancellationTokenSource();
        var tip = new LongShot.LongShotProgressWindow();
        tip.Cancelled += cts.Cancel;

        foreach (var w in _windows) w.Hide();

        double scale = _windows.FirstOrDefault(w => w.Monitor.BoundsPx.Contains(sel.Location))?.Monitor.DpiScale
                       ?? _windows[0].Monitor.DpiScale;
        tip.Show();
        tip.PlaceNear(sel, scale);

        LongShot.LongShotOutcome? outcome = null;
        try
        {
            // 让隐藏动作先落到屏幕上，否则第一帧会把覆盖层自己拍进去
            await Task.Delay(180, cts.Token);
            var progress = new Progress<string>(tip.Report);
            outcome = await LongShot.LongShotRunner.RunAsync(sel, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Core.TraceLog.Log("LongShot cancelled by user");
        }
        catch (Exception ex)
        {
            Core.TraceLog.Log($"LongShot error: {ex}");
            MessageBox.Show($"长截图失败：{ex.Message}", "WeCapture",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            tip.Close();
            cts.Dispose();
            _longShotRunning = false;
        }

        ExitAll();

        if (outcome is { Success: true, Image: not null })
        {
            // 长图不能直接钉出来：几千像素高会被整体缩到看不清，用带缩放的预览窗
            ClipboardHelper.SetImage(outcome.Image);
            new LongShot.LongShotPreviewWindow(outcome.Image, _settings).Show();
        }
        else if (outcome is { Success: false })
        {
            // 明确报错，绝不给出一张静默拼错的图
            MessageBox.Show(outcome.Message, "长截图未完成",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    // ================= 结束 =================

    public void ExitAll()
    {
        if (_closing) return;
        _closing = true;
        Core.TraceLog.Log("ExitAll called");

        foreach (var w in _windows)
            w.Close();
        _windows.Clear();

        _monitors.Dispose();
        _mosaicCache = null;

        if (ReferenceEquals(_active, this))
            _active = null;
    }
}
