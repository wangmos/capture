using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeCapture.Annotations;
using WeCapture.Core;

namespace WeCapture.Viewer;

/// <summary>
/// 图片之上的标注层。标注存的是图片像素坐标，这里统一套一个 ScaleTransform(zoom)
/// 画回去——所以缩放、滚动都不需要标注自己关心。
/// </summary>
public sealed class ViewerAnnotationLayer : FrameworkElement
{
    private ViewerEditor? _editor;
    private BitmapSource? _mosaic;
    private int _imageW, _imageH;

    /// <summary>当前缩放比例（图片像素 → 显示像素）。</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>最近一次鼠标位置（图片坐标），用于绘制拖拽预览。</summary>
    public PointI LastMouse { get; set; }

    public void Attach(ViewerEditor editor, int imageWidth, int imageHeight, BitmapSource? mosaic)
    {
        _editor = editor;
        _imageW = imageWidth;
        _imageH = imageHeight;
        _mosaic = mosaic;
        editor.Changed += InvalidateVisual;
    }

    public void Detach()
    {
        if (_editor != null) _editor.Changed -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var editor = _editor;
        if (editor == null) return;
        if (editor.Annotations.Count == 0 && !editor.IsDrawing) return;

        dc.PushTransform(new ScaleTransform(Zoom, Zoom));

        var env = new RenderEnv(new RectI(0, 0, _imageW, _imageH), _mosaic, 1.0);
        foreach (var a in editor.Annotations)
            a.Render(dc, in env);

        if (editor.IsDrawing)
            editor.BuildPreview(LastMouse)?.Render(dc, in env);

        dc.Pop();
    }
}
