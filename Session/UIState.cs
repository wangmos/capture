namespace WeCapture.Session;

public enum UIState
{
    /// <summary>无选区：悬停探测窗口、拖拽新建选区。</summary>
    Idle,

    /// <summary>正在拖拽创建选区。</summary>
    Selecting,

    /// <summary>选区已确定：可调整、标注、确认。</summary>
    Selected,

    /// <summary>文字工具编辑中。</summary>
    TextEditing,
}

public enum Tool
{
    None,
    Rectangle,
    Ellipse,
    Arrow,
    Pen,
    Text,
    Mosaic,
    Number,
}

public enum DragMode
{
    None,
    NewSelect,
    Move,
    ResizeLeft,
    ResizeTop,
    ResizeRight,
    ResizeBottom,
    ResizeTopLeft,
    ResizeTopRight,
    ResizeBottomLeft,
    ResizeBottomRight,
    Draw,

    /// <summary>Selected 状态选区外按下、尚未决定是"扩展"还是"重选"。</summary>
    ExpandPending,
}
