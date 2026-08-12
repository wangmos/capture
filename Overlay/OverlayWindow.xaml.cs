using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WeCapture.Capture;
using WeCapture.Core;
using WeCapture.Native;
using WeCapture.Session;
using WeCapture.Toolbar;

namespace WeCapture.Overlay;

/// <summary>单个显示器的全屏覆盖窗口：事件路由、提示、工具条定位、文字编辑。</summary>
public partial class OverlayWindow : Window
{
    private readonly CaptureSession _session;
    private readonly HoverDetector _hover;
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _topmostTimer;
    private DateTime _lastHoverDetect = DateTime.MinValue;
    private PointI _lastMouseGlobal;
    private bool _mouseSeen;
    private int _downClickCount;

    public MonitorShot Monitor { get; }
    private SessionModel Model => _session.Model;
    private double Scale => Monitor.DpiScale;

    public OverlayWindow(CaptureSession session, MonitorShot monitor, HoverDetector hover)
    {
        InitializeComponent();
        _session = session;
        Monitor = monitor;
        _hover = hover;

        BgImage.Source = monitor.Image;
        Layer.Attach(session, monitor);

        // 覆盖层不是文本输入面：关掉输入法，避免中文输入法拦截字母快捷键。
        // 文字标注框需要输入中文，单独放开。
        InputMethod.SetIsInputMethodEnabled(this, false);
        InputMethod.SetIsInputMethodEnabled(TextEdit, true);

        Model.Changed += OnModelChanged;
        TextEdit.KeyDown += OnTextEditKeyDown;
        TextEdit.LostFocus += (_, _) => CommitTextIfEditing();

        // 鼠标静止时也要推进悬停防抖（探测只在移动事件里发生会导致永远不提交）
        _hoverTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(50) };
        _hoverTimer.Tick += (_, _) => DetectHoverIfIdle();

        // 会话期间保持最前：置顶画中画等窗口可能抢 Z 序，定期重置顶。
        // 注意不能用 Topmost=false→true：那会让覆盖层瞬间掉出置顶层，
        // 下面正在播放的视频/动画就会以定时器频率闪出来（表现为频闪）。
        // SetWindowPos(HWND_TOPMOST) 是幂等的，全程不离开置顶层，因此无闪烁。
        _topmostTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(400) };
        _topmostTimer.Tick += (_, _) =>
        {
            if (!Topmost) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        };

        Closed += (_, _) =>
        {
            _hoverTimer.Stop();
            _topmostTimer.Stop();
            Model.Changed -= OnModelChanged;
            Layer.Detach();
        };
    }

    private void DetectHoverIfIdle()
    {
        if (!_mouseSeen) return;
        if (Model.State != UIState.Idle) return;
        if (Visibility != Visibility.Visible) return;
        Model.SetHover(_hover.Detect(_lastMouseGlobal));
    }

    // ================= 坐标换算 =================

    private PointI ToGlobalPx(System.Windows.Point dipLocal) => new(
        Monitor.BoundsPx.X + (int)Math.Round(dipLocal.X * Scale),
        Monitor.BoundsPx.Y + (int)Math.Round(dipLocal.Y * Scale));

    private System.Windows.Point ToLocalDip(PointI g) => new(
        (g.X - Monitor.BoundsPx.X) / Scale,
        (g.Y - Monitor.BoundsPx.Y) / Scale);

    /// <summary>本窗口的原生句柄（供悬停探测排除）。</summary>
    public IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();

    // ================= 精确铺满显示器 =================

    public void PlaceExactly()
    {
        var b = Monitor.BoundsPx;
        double s = Scale;
        Left = b.X / s;
        Top = b.Y / s;
        Width = b.W / s;
        Height = b.H / s;
        Show();
        CorrectPlacement();
        _hoverTimer.Start();
        _topmostTimer.Start();
        WeCapture.Core.TraceLog.Log($"OverlayWindow shown mon={b.X},{b.Y},{b.W}x{b.H} s={s} hwnd={Hwnd}");
    }

    private void CorrectPlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetWindowRect(hwnd, out RECT r)) return;

        var b = Monitor.BoundsPx;
        double s = Scale;
        double dx = (b.Left - r.Left) / s;
        double dy = (b.Top - r.Top) / s;
        double dw = (b.W - r.Width) / s;
        double dh = (b.H - r.Height) / s;

        if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01 || Math.Abs(dw) > 0.01 || Math.Abs(dh) > 0.01)
        {
            Left += dx;
            Top += dy;
            Width += dw;
            Height += dh;
        }
    }

    public void FocusOverlay()
    {
        Activate();
        Focus();
    }

    /// <summary>确保键盘焦点在本窗口（否则 Esc / 快捷键会被丢给上一个前台程序）。</summary>
    private void EnsureKeyboardFocus()
    {
        if (IsKeyboardFocusWithin) return;
        Activate();
        Keyboard.Focus(this);
    }

    // ================= 鼠标路由 =================

    private static bool IsChrome(DependencyObject? o)
    {
        while (o != null)
        {
            if (o is ToolbarControl || o is System.Windows.Controls.Primitives.Popup || o is TextBox)
                return true;
            o = VisualTreeHelper.GetParent(o);
        }
        return false;
    }

    private void OnPreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (IsChrome(e.OriginalSource as DependencyObject))
        {
            _downClickCount = 0;
            return;
        }

        // 覆盖层是 ShowActivated=False 显示的，热键若来自别的前台程序，系统会抑制
        // SetForegroundWindow，键盘焦点就不在这里——那样 Esc / 快捷键全部失灵。
        // 用户一旦在覆盖层上按下鼠标，就把焦点收回来。
        EnsureKeyboardFocus();

        // WPF 的 ButtonUp 事件 ClickCount 恒为 1，双击判定需借用 Down 的计数
        _downClickCount = e.ClickCount;

        var gpt = ToGlobalPx(e.GetPosition(this));
        if (Model.State == UIState.TextEditing)
            CommitTextIfEditing();

        Model.OnLeftDown(gpt, e.ClickCount);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (IsChrome(e.OriginalSource as DependencyObject)) return;

        var gpt = ToGlobalPx(e.GetPosition(this));
        Model.OnLeftUp(gpt, Math.Max(e.ClickCount, _downClickCount));
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnPreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (IsChrome(e.OriginalSource as DependencyObject)) return;

        if (Model.State == UIState.TextEditing)
            CommitTextIfEditing();

        Model.OnRightDown();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (IsChrome(e.OriginalSource as DependencyObject))
        {
            Cursor = Cursors.Arrow;
            CursorTip.Visibility = Visibility.Hidden;
            SizeTip.Visibility = Visibility.Hidden;
            return;
        }

        var dip = e.GetPosition(this);
        var gpt = ToGlobalPx(dip);
        Layer.LastMouseGlobal = gpt;
        _lastMouseGlobal = gpt;
        _mouseSeen = true;

        Model.OnMouseMove(gpt);

        // Idle：窗口/控件悬停探测（40ms 节流，探测器内部再防抖 60ms；静止时由定时器续推）
        if (Model.State == UIState.Idle)
        {
            var now = DateTime.Now;
            if ((now - _lastHoverDetect).TotalMilliseconds >= 40)
            {
                _lastHoverDetect = now;
                Model.SetHover(_hover.Detect(gpt));
            }
        }

        Cursor = Model.GetDesiredCursor(gpt);
        UpdateTips(gpt, dip);
    }

    // ================= 提示 =================

    private void UpdateTips(PointI gpt, System.Windows.Point dip)
    {
        var m = Model;

        if (m.State == UIState.Idle)
        {
            CursorTip.Update(gpt.X, gpt.Y, _session.Monitors.GetPixel(gpt.X, gpt.Y));
            CursorTip.Visibility = Visibility.Visible;
            PositionNearCursor(CursorTip, dip);
        }
        else
        {
            CursorTip.Visibility = Visibility.Hidden;
        }

        bool showSize = m.Selection is RectI s && !s.IsEmpty &&
                        (m.State == UIState.Selecting ||
                         m.DragMode is DragMode.Move
                             or DragMode.ResizeLeft or DragMode.ResizeTop
                             or DragMode.ResizeRight or DragMode.ResizeBottom
                             or DragMode.ResizeTopLeft or DragMode.ResizeTopRight
                             or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight);
        if (showSize && m.Selection is RectI s2)
        {
            SizeTip.Update(s2.W, s2.H);
            SizeTip.Visibility = Visibility.Visible;
            PositionNearCursor(SizeTip, dip);
        }
        else
        {
            SizeTip.Visibility = Visibility.Hidden;
        }
    }

    private void PositionNearCursor(FrameworkElement el, System.Windows.Point cursorDip)
    {
        el.UpdateLayout();
        double w = el.ActualWidth, h = el.ActualHeight;
        double mw = Monitor.BoundsPx.W / Scale;
        double mh = Monitor.BoundsPx.H / Scale;

        double x = cursorDip.X + 18;
        double y = cursorDip.Y + 22;
        if (x + w > mw - 4) x = cursorDip.X - w - 14;
        if (y + h > mh - 4) y = cursorDip.Y - h - 16;

        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
    }

    // ================= 键盘 =================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 文字编辑期间：Enter/Esc 交给 TextEdit 处理，其余按键透传给 TextBox
        if (TextEdit.Visibility == Visibility.Visible && TextEdit.IsKeyboardFocusWithin)
            return;

        if (Model.OnKey(ResolveKey(e), Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>
    /// 还原真实按键。中文输入法开启时字母键会被 IME 吃掉，WPF 只报 Key.ImeProcessed，
    /// 单字母快捷键就会全部失效——必须从 ImeProcessedKey 取回原始键。
    /// </summary>
    private static Key ResolveKey(KeyEventArgs e) => e.Key switch
    {
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.System => e.SystemKey,
        _ => e.Key,
    };

    // ================= 文字编辑 =================

    public void ShowTextEdit(PointI gpos)
    {
        var m = Model;
        var p = ToLocalDip(gpos);
        double fontDip = m.FontSizePx / Scale;

        var brush = new SolidColorBrush(m.DrawColor);
        TextEdit.Foreground = brush;
        TextEdit.CaretBrush = brush;
        TextEdit.FontSize = fontDip;

        if (m.Selection is RectI sel)
        {
            double maxW = (sel.Right - gpos.X) / Scale;
            TextEdit.MaxWidth = Math.Clamp(maxW, 60, 2000);
        }

        Canvas.SetLeft(TextEdit, p.X);
        Canvas.SetTop(TextEdit, p.Y - fontDip * 0.12);
        TextEdit.Text = "";
        TextEdit.Visibility = Visibility.Visible;
        // 延迟聚焦：PreviewMouseDown 期间同步 Focus 会被随后的鼠标处理抢走
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (TextEdit.Visibility == Visibility.Visible)
            {
                TextEdit.Focus();
                TextEdit.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitTextIfEditing()
    {
        if (TextEdit.Visibility != Visibility.Visible) return;
        string text = TextEdit.Text;
        TextEdit.Visibility = Visibility.Hidden;
        Model.CommitText(text);
        Refocus();
    }

    /// <summary>文本框隐藏后收回键盘焦点，避免焦点悬空导致前台被其他窗口抢走。</summary>
    private void Refocus()
    {
        Focus();
        Keyboard.Focus(this);
    }

    private void OnTextEditKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTextIfEditing();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (TextEdit.Visibility == Visibility.Visible)
            {
                TextEdit.Visibility = Visibility.Hidden;
                Model.CancelText();
                Refocus();
            }
            e.Handled = true;
        }
    }

    // ================= 工具条 =================

    private void OnModelChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshChrome);
            return;
        }
        RefreshChrome();
    }

    private void RefreshChrome()
    {
        var m = Model;

        if (m.State is UIState.Selected or UIState.TextEditing && m.Selection is RectI sel && !sel.IsEmpty)
        {
            // 工具条宿主：包含选区右下角的屏幕
            bool isHost = Monitor.BoundsPx.Contains(new PointI(sel.Right - 1, sel.Bottom - 1));
            if (isHost)
            {
                Toolbar.RefreshFrom(m);
                Toolbar.Visibility = Visibility.Visible;
                Toolbar.UpdateLayout();
                PlaceToolbar(sel, Toolbar.ActualWidth, Toolbar.ActualHeight);
                // 布局落定后再记按钮坐标（内部去重，只在变化时写日志）
                Dispatcher.BeginInvoke(new System.Action(Toolbar.LogButtonRects),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
        }
        Toolbar.Visibility = Visibility.Collapsed;
    }

    private void PlaceToolbar(RectI sel, double twDip, double thDip)
    {
        var b = Monitor.BoundsPx;
        double tw = Math.Max(1, twDip) * Scale;
        double th = Math.Max(1, thDip) * Scale;
        double gap = 10 * Scale;

        int gx = sel.Right - (int)tw;
        int gy = sel.Bottom + (int)gap;

        if (gy + th > b.Bottom) gy = sel.Top - (int)(th + gap);   // 下方放不下 → 翻到上方
        if (gy < b.Top) gy = sel.Bottom - (int)(th + gap);        // 上方也放不下 → 选区内下沿
        gx = Math.Clamp(gx, b.Left + 4, Math.Max(b.Left + 4, b.Right - (int)tw - 4));
        gy = Math.Max(gy, b.Top + 4);

        var p = ToLocalDip(new PointI(gx, gy));
        Canvas.SetLeft(Toolbar, p.X);
        Canvas.SetTop(Toolbar, p.Y);
    }
}
