using System.Windows.Media;
using WeCapture.Annotations;
using WeCapture.Core;

namespace WeCapture.Session;

/// <summary>
/// 截图会话的状态机与数据容器。
/// 所有坐标为虚拟屏全局物理像素。视图（OverlayWindow）把鼠标/键盘事件路由到这里。
/// </summary>
public sealed class SessionModel
{
    private const int MinSelectSize = 3;
    private const int MoveThreshold = 4;

    public RectI VirtualBounds { get; }

    public UIState State { get; private set; } = UIState.Idle;
    public RectI? Selection { get; private set; }
    public RectI? HoverRect { get; private set; }
    public DragMode DragMode { get; private set; } = DragMode.None;
    public Tool ActiveTool { get; private set; } = Tool.None;

    // ---------- 样式 ----------
    public Color DrawColor { get; set; } = Color.FromRgb(0xFF, 0x3B, 0x30);
    public double ThicknessPx { get; set; } = 5;
    public double FontSizePx { get; set; } = 24;
    public double MosaicRadiusPx { get; set; } = 20;

    // ---------- 标注 ----------
    public List<Annotation> Annotations { get; } = new();
    public bool CanUndo => Annotations.Count > 0;

    // ---------- 拖拽内部状态 ----------
    private PointI _anchor;
    private RectI _moveOrigin;
    private RectI _resizeOrigin;
    private PointI _drawStart;
    private List<PointI>? _strokePoints;
    private RectI? _pendingHoverClick;
    private bool _gestureMoved;

    // ---------- 文字编辑 ----------
    public PointI TextEditPos { get; private set; }

    // ---------- 事件 ----------
    /// <summary>模型变化（重绘/工具条/提示刷新）。</summary>
    public event Action? Changed;

    /// <summary>双击选区 / Enter 确认：复制并退出。</summary>
    public event Action? CopyConfirmed;

    /// <summary>请求结束会话（右键/Esc/工具条退出）。</summary>
    public event Action? ExitRequested;

    /// <summary>请求打开文字编辑框（全局坐标）。</summary>
    public event Action<PointI>? TextEditRequested;

    public SessionModel(RectI virtualBounds)
    {
        VirtualBounds = virtualBounds;
    }

    // ========== 悬停探测 ==========

    public void SetHover(RectI? rect)
    {
        if (State != UIState.Idle) return;
        if (HoverRect == rect) return;
        HoverRect = rect;
        RaiseChanged();
    }

    // ========== 鼠标 ==========

    public void OnLeftDown(PointI gpt, int clickCount)
    {
        _gestureMoved = false;
        _anchor = gpt;

        // 双击选区内部 → 复制退出（Down 的 ClickCount 可靠；任意标注工具下都生效）
        if (clickCount >= 2 && State == UIState.Selected &&
            Selection is RectI dcs && dcs.Contains(gpt))
        {
            CopyConfirmed?.Invoke();
            return;
        }

        switch (State)
        {
            case UIState.Idle:
                _pendingHoverClick = HoverRect is RectI hv && hv.Contains(gpt) ? hv : null;
                HoverRect = null;
                Selection = new RectI(gpt.X, gpt.Y, 0, 0);
                State = UIState.Selecting;
                DragMode = DragMode.NewSelect;
                break;

            case UIState.Selected:
            case UIState.TextEditing: // 窗口已在路由前提交文字
                if (Selection is not RectI sel) goto case UIState.Idle;
                var hit = SelectionHitTester.HitTest(sel, gpt);

                // 标号工具：单击放置序号（手柄仍可缩放；区域外单击自动扩展选区）
                if (ActiveTool == Tool.Number &&
                    hit is not (>= DragMode.ResizeLeft and <= DragMode.ResizeBottomRight))
                {
                    PlaceNumber(sel, gpt, outside: hit == DragMode.None);
                    break;
                }

                switch (hit)
                {
                    case DragMode.Move:
                        if (ActiveTool == Tool.None)
                        {
                            DragMode = DragMode.Move;
                            _moveOrigin = sel;
                        }
                        else if (ActiveTool == Tool.Text)
                        {
                            TextEditPos = gpt;
                            State = UIState.TextEditing;
                            TextEditRequested?.Invoke(gpt);
                        }
                        else
                        {
                            StartStroke(gpt);
                        }
                        break;

                    case DragMode.None: // 选区外按下：单击扩展选区，拖动则清空标注重开选择
                        DragMode = DragMode.ExpandPending;
                        break;

                    default: // 各方向手柄
                        DragMode = hit;
                        _resizeOrigin = sel;
                        break;
                }
                break;

            case UIState.Selecting:
                break;
        }
        RaiseChanged();
    }

    private void StartStroke(PointI gpt)
    {
        DragMode = DragMode.Draw;
        _drawStart = gpt;
        _strokePoints = new List<PointI> { gpt };
    }

    /// <summary>标号工具：放置递增序号；点击在选区外时先把选区扩展到徽章范围。</summary>
    private void PlaceNumber(RectI sel, PointI gpt, bool outside)
    {
        int idx = 0;
        foreach (var a in Annotations)
            if (a is NumberAnnotation) idx++;
        idx++;

        var ann = new NumberAnnotation { Center = gpt, Index = idx, Color = DrawColor };

        if (outside)
        {
            var u = sel.Union(ann.BoundsPx);
            int l = Math.Max(u.Left, VirtualBounds.Left);
            int t = Math.Max(u.Top, VirtualBounds.Top);
            int r = Math.Min(u.Right, VirtualBounds.Right);
            int b = Math.Min(u.Bottom, VirtualBounds.Bottom);
            Selection = RectI.FromLTRB(l, t, r, b);
        }

        Annotations.Add(ann);
        TraceLog.Log($"Number placed idx={idx} at {gpt} outside={outside} sel={Selection}");
    }

    public void OnMouseMove(PointI gpt)
    {
        if (DragMode != DragMode.None &&
            (Math.Abs(gpt.X - _anchor.X) > MoveThreshold || Math.Abs(gpt.Y - _anchor.Y) > MoveThreshold))
            _gestureMoved = true;

        switch (DragMode)
        {
            case DragMode.NewSelect:
                Selection = RectI.Normalize(_anchor, gpt).ClampInto(VirtualBounds);
                break;

            case DragMode.ExpandPending:
                if (_gestureMoved)
                {
                    // 拖动 → 视为重新框选（同微信：清空标注）
                    Annotations.Clear();
                    ActiveTool = Tool.None;
                    State = UIState.Selecting;
                    DragMode = DragMode.NewSelect;
                    _pendingHoverClick = null;
                    Selection = RectI.Normalize(_anchor, gpt).ClampInto(VirtualBounds);
                }
                break;

            case DragMode.Move:
                if (Selection is RectI ms)
                    Selection = _moveOrigin.Offset(gpt.X - _anchor.X, gpt.Y - _anchor.Y)
                        .ClampInto(VirtualBounds);
                break;

            case >= DragMode.ResizeLeft and <= DragMode.ResizeBottomRight:
                ApplyResize(gpt);
                break;

            case DragMode.Draw:
                if (_strokePoints != null)
                {
                    var last = _strokePoints[^1];
                    if (Math.Abs(gpt.X - last.X) >= 1 || Math.Abs(gpt.Y - last.Y) >= 1)
                        _strokePoints.Add(gpt);
                }
                break;
        }

        if (DragMode != DragMode.None)
            RaiseChanged();
    }

    private void ApplyResize(PointI gpt)
    {
        int l = _resizeOrigin.Left, t = _resizeOrigin.Top;
        int r = _resizeOrigin.Right, b = _resizeOrigin.Bottom;
        var vb = VirtualBounds;

        switch (DragMode)
        {
            case DragMode.ResizeLeft: l = gpt.X; break;
            case DragMode.ResizeRight: r = gpt.X; break;
            case DragMode.ResizeTop: t = gpt.Y; break;
            case DragMode.ResizeBottom: b = gpt.Y; break;
            case DragMode.ResizeTopLeft: l = gpt.X; t = gpt.Y; break;
            case DragMode.ResizeTopRight: r = gpt.X; t = gpt.Y; break;
            case DragMode.ResizeBottomLeft: l = gpt.X; b = gpt.Y; break;
            case DragMode.ResizeBottomRight: r = gpt.X; b = gpt.Y; break;
        }

        // 限制在虚拟屏内
        l = Math.Clamp(l, vb.Left, vb.Right);
        r = Math.Clamp(r, vb.Left, vb.Right);
        t = Math.Clamp(t, vb.Top, vb.Bottom);
        b = Math.Clamp(b, vb.Top, vb.Bottom);

        // 不允许翻转，保持最小尺寸
        if (r - l < MinSelectSize) r = l == vb.Left ? l + MinSelectSize : l;
        if (l > r - MinSelectSize) l = r - MinSelectSize;
        if (b - t < MinSelectSize) b = t == vb.Top ? t + MinSelectSize : t;
        if (t > b - MinSelectSize) t = b - MinSelectSize;

        Selection = RectI.FromLTRB(l, t, r, b);
    }

    public void OnLeftUp(PointI gpt, int clickCount)
    {
        var mode = DragMode;
        DragMode = DragMode.None;

        switch (mode)
        {
            case DragMode.NewSelect:
                if (Selection is RectI ns && ns.W >= MinSelectSize && ns.H >= MinSelectSize)
                {
                    State = UIState.Selected;
                }
                else if (_pendingHoverClick is RectI hv)
                {
                    Selection = hv.Intersect(VirtualBounds);
                    State = Selection is RectI s2 && !s2.IsEmpty ? UIState.Selected : UIState.Idle;
                }
                else
                {
                    Selection = null;
                    State = UIState.Idle;
                }
                _pendingHoverClick = null;
                break;

            case DragMode.Move:
            case >= DragMode.ResizeLeft and <= DragMode.ResizeBottomRight:
                break;

            case DragMode.ExpandPending:
                // 单击（未拖动）：把选区边界扩展到点击位置，保留标注与工具
                if (Selection is RectI es)
                    Selection = ExpandSelection(es, gpt);
                break;

            case DragMode.Draw:
                FinishStroke(gpt);
                break;
        }

        // 双击选区内部（无工具、未移动）→ 复制退出
        if (State == UIState.Selected && clickCount >= 2 && !_gestureMoved &&
            ActiveTool == Tool.None && Selection is RectI ds && ds.Contains(gpt))
        {
            RaiseChanged();
            CopyConfirmed?.Invoke();
            return;
        }

        RaiseChanged();
    }

    /// <summary>选区外单击：把点击位置之外的边扩展到点击点（点在角外则同时扩展两边）。</summary>
    private RectI ExpandSelection(RectI sel, PointI gpt)
    {
        int l = sel.Left, t = sel.Top, r = sel.Right, b = sel.Bottom;
        if (gpt.X < l) l = gpt.X;
        if (gpt.X > r) r = gpt.X;
        if (gpt.Y < t) t = gpt.Y;
        if (gpt.Y > b) b = gpt.Y;

        var vb = VirtualBounds;
        l = Math.Clamp(l, vb.Left, vb.Right);
        t = Math.Clamp(t, vb.Top, vb.Bottom);
        r = Math.Clamp(r, vb.Left, vb.Right);
        b = Math.Clamp(b, vb.Top, vb.Bottom);

        TraceLog.Log($"Selection expanded to ({l},{t},{r - l}x{b - t}) by click at {gpt}");
        return RectI.FromLTRB(l, t, r, b);
    }

    private void FinishStroke(PointI gpt)
    {
        var pts = _strokePoints;
        _strokePoints = null;
        if (pts == null || Selection is not RectI sel) return;

        Annotation? ann = ActiveTool switch
        {
            Tool.Rectangle when SizeOk(_anchor, gpt) => new RectAnnotation
            {
                P1 = _anchor, P2 = gpt, Color = DrawColor, ThicknessPx = ThicknessPx,
            },
            Tool.Ellipse when SizeOk(_anchor, gpt) => new EllipseAnnotation
            {
                P1 = _anchor, P2 = gpt, Color = DrawColor, ThicknessPx = ThicknessPx,
            },
            Tool.Arrow when SizeOk(_anchor, gpt) => new ArrowAnnotation
            {
                From = _anchor, To = gpt, Color = DrawColor, ThicknessPx = ThicknessPx,
            },
            Tool.Pen when pts.Count >= 2 => new FreehandAnnotation
            {
                Points = pts, Color = DrawColor, ThicknessPx = ThicknessPx,
            },
            Tool.Mosaic when SizeOk(_anchor, gpt) => new MosaicAnnotation
            {
                P1 = _anchor, P2 = gpt,
            },
            _ => null,
        };

        if (ann != null)
            Annotations.Add(ann);

        static bool SizeOk(PointI a, PointI b) =>
            Math.Abs(a.X - b.X) >= MinSelectSize || Math.Abs(a.Y - b.Y) >= MinSelectSize;
    }

    public void OnRightDown()
    {
        switch (State)
        {
            case UIState.Selecting:
                State = UIState.Idle;
                Selection = null;
                DragMode = DragMode.None;
                break;

            case UIState.Selected:
            case UIState.TextEditing:
                State = UIState.Idle;
                Selection = null;
                Annotations.Clear();
                ActiveTool = Tool.None;
                DragMode = DragMode.None;
                break;

            case UIState.Idle:
                ExitRequested?.Invoke();
                return;
        }
        RaiseChanged();
    }

    // ========== 键盘 ==========

    /// <returns>是否已处理。</returns>
    public bool OnKey(System.Windows.Input.Key key)
    {
        switch (key)
        {
            case System.Windows.Input.Key.Escape:
                switch (State)
                {
                    case UIState.Selecting:
                        State = UIState.Idle;
                        Selection = null;
                        DragMode = DragMode.None;
                        RaiseChanged();
                        return true;

                    case UIState.Selected:
                        if (ActiveTool != Tool.None)
                            SetTool(Tool.None);
                        else if (Selection != null)
                        {
                            Selection = null;
                            Annotations.Clear();
                        }
                        else
                        {
                            ExitRequested?.Invoke();
                            return true;
                        }
                        RaiseChanged();
                        return true;

                    case UIState.Idle:
                        ExitRequested?.Invoke();
                        return true;
                }
                return false;

            case System.Windows.Input.Key.Enter:
                if (State == UIState.Selected && Selection != null)
                {
                    CopyConfirmed?.Invoke();
                    return true;
                }
                return false;
        }
        return false;
    }

    // ========== 工具 / 样式 / 撤销 ==========

    public void SetTool(Tool tool)
    {
        ActiveTool = ActiveTool == tool ? Tool.None : tool;
        TraceLog.Log($"SetTool {tool} active={ActiveTool}");
        RaiseChanged();
    }

    public void SetDrawColor(Color c)
    {
        DrawColor = c;
        RaiseChanged();
    }

    public void SetThickness(double px)
    {
        ThicknessPx = px;
        RaiseChanged();
    }

    public void SetFontSize(double px)
    {
        FontSizePx = px;
        RaiseChanged();
    }

    public void SetMosaicRadius(double px)
    {
        MosaicRadiusPx = px;
        RaiseChanged();
    }

    public void Undo()
    {
        if (Annotations.Count > 0)
        {
            Annotations.RemoveAt(Annotations.Count - 1);
            RaiseChanged();
        }
    }

    // ========== 文字编辑 ==========

    public void CommitText(string text)
    {
        if (State != UIState.TextEditing) return;
        State = UIState.Selected;
        if (!string.IsNullOrWhiteSpace(text))
        {
            Annotations.Add(new TextAnnotation
            {
                Position = TextEditPos,
                Text = text.TrimEnd('\r', '\n'),
                FontSizePx = FontSizePx,
                Color = DrawColor,
            });
        }
        RaiseChanged();
    }

    public void CancelText()
    {
        if (State != UIState.TextEditing) return;
        State = UIState.Selected;
        RaiseChanged();
    }

    // ========== 预览（拖拽中的临时标注） ==========

    public Annotation? BuildPreviewAnnotation(PointI gpt)
    {
        if (DragMode != DragMode.Draw || _strokePoints == null) return null;

        return ActiveTool switch
        {
            Tool.Rectangle => new RectAnnotation { P1 = _drawStart, P2 = gpt, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Ellipse => new EllipseAnnotation { P1 = _drawStart, P2 = gpt, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Arrow => new ArrowAnnotation { From = _drawStart, To = gpt, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Pen => new FreehandAnnotation { Points = _strokePoints, Color = DrawColor, ThicknessPx = ThicknessPx },
            Tool.Mosaic => new MosaicAnnotation { P1 = _drawStart, P2 = gpt },
            _ => null,
        };
    }

    // ========== 光标 ==========

    public System.Windows.Input.Cursor GetDesiredCursor(PointI gpt)
    {
        switch (State)
        {
            case UIState.Idle:
            case UIState.Selecting:
                return System.Windows.Input.Cursors.Cross;

            case UIState.Selected:
            case UIState.TextEditing:
                if (Selection is RectI sel)
                {
                    var hit = SelectionHitTester.HitTest(sel, gpt);
                    switch (hit)
                    {
                        case DragMode.ResizeLeft:
                        case DragMode.ResizeRight: return System.Windows.Input.Cursors.SizeWE;
                        case DragMode.ResizeTop:
                        case DragMode.ResizeBottom: return System.Windows.Input.Cursors.SizeNS;
                        case DragMode.ResizeTopLeft:
                        case DragMode.ResizeBottomRight: return System.Windows.Input.Cursors.SizeNWSE;
                        case DragMode.ResizeTopRight:
                        case DragMode.ResizeBottomLeft: return System.Windows.Input.Cursors.SizeNESW;
                        case DragMode.Move:
                            return ActiveTool == Tool.None
                                ? System.Windows.Input.Cursors.SizeAll
                                : System.Windows.Input.Cursors.Cross;
                    }

                    // 选区外：显示扩展方向光标（<-> 水平 / 垂直 / 对角）
                    if (hit == DragMode.None && ActiveTool != Tool.Number)
                    {
                        bool lo = gpt.X < sel.Left, ro = gpt.X > sel.Right;
                        bool to = gpt.Y < sel.Top, bo = gpt.Y > sel.Bottom;
                        if ((lo && to) || (ro && bo)) return System.Windows.Input.Cursors.SizeNWSE;
                        if ((ro && to) || (lo && bo)) return System.Windows.Input.Cursors.SizeNESW;
                        if (lo || ro) return System.Windows.Input.Cursors.SizeWE;
                        if (to || bo) return System.Windows.Input.Cursors.SizeNS;
                    }
                }
                return System.Windows.Input.Cursors.Cross;
        }
        return System.Windows.Input.Cursors.Arrow;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
