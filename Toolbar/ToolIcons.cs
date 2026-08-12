using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WeCapture.Toolbar;

/// <summary>纯几何图标（无外部资源）。</summary>
public static class ToolIcons
{
    private const double StrokeW = 1.6;

    public static UIElement Create(Session.Tool tool) => tool switch
    {
        Session.Tool.Rectangle => StrokePath("M2.5,3.5 H13.5 V12.5 H2.5 Z"),
        Session.Tool.Ellipse => new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = StrokeW, Margin = new Thickness(1) },
        Session.Tool.Arrow => StrokePath("M2.5,13.5 L12.5,3.5 M12.5,3.5 H7.5 M12.5,3.5 V8.5"),
        Session.Tool.Pen => StrokePath("M3,13 L3.6,10.4 L10.8,3.2 L12.8,5.2 L5.6,12.4 Z M9.6,4.4 L11.6,6.4"),
        Session.Tool.Mosaic => MosaicIcon(),
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    };

    public static UIElement Undo() => StrokePath("M5.5,4.5 H11 A3.5,3.5 0 0 1 11,11.5 H6 M8,2 L5.5,4.5 L8,7");

    public static UIElement Ocr() => TextGlyph("文", 13);

    /// <summary>取字：文本光标（I 形）。</summary>
    public static UIElement TextSelect() => StrokePath("M6,3 H10 M8,3 V13 M6,13 H10");

    public static UIElement Pin()
    {
        var g = new Grid { Width = 16, Height = 16 };
        g.Children.Add(new Ellipse
        {
            Width = 7, Height = 7, Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 1, 0),
        });
        g.Children.Add(StrokePath("M10,6 L4,12.5 M6.2,9.8 L4,12.5"));
        return g;
    }

    public static UIElement Save() => StrokePath("M8,2.5 V10 M4.8,7.2 L8,10.4 L11.2,7.2 M2.5,13 H13.5");

    /// <summary>长截图：两条内容线 + 向下延伸的箭头。</summary>
    public static UIElement LongShot() =>
        StrokePath("M3.5,2.8 H12.5 M3.5,5.6 H12.5 M8,7.4 V13.2 M5.6,10.8 L8,13.2 L10.4,10.8");

    public static UIElement Copy()
    {
        var g = new Grid { Width = 16, Height = 16 };
        g.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 9, Height = 10, Stroke = Brushes.White, StrokeThickness = StrokeW,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        });
        g.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 9, Height = 10, Stroke = Brushes.White, StrokeThickness = StrokeW,
            Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0x2C, 0x2C, 0x2C)),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        });
        return g;
    }

    public static UIElement Exit() => StrokePath("M4,4 L12,12 M12,4 L4,12");

    /// <summary>标号：圆圈内含数字 1。</summary>
    public static UIElement Number()
    {
        var g = new Grid { Width = 16, Height = 16 };
        g.Children.Add(new Ellipse
        {
            Width = 14, Height = 14,
            Stroke = Brushes.White, StrokeThickness = StrokeW,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var t = TextGlyph("1", 9);
        if (t is System.Windows.Controls.TextBlock tb)
        {
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.Margin = new Thickness(0, 1, 0, 0);
        }
        g.Children.Add(t);
        return g;
    }

    public static UIElement TextGlyph(string text, double fontSize) => new System.Windows.Controls.TextBlock
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = fontSize,
        FontFamily = new FontFamily("Microsoft YaHei"),
        FontWeight = FontWeights.Bold,
    };

    private static UIElement MosaicIcon()
    {
        var g = new Grid { Width = 16, Height = 16 };
        for (int i = 0; i < 4; i++)
        {
            g.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 5, Height = 5,
                Fill = Brushes.White,
                Opacity = (i % 2 == 0) ? 1.0 : 0.45,
                HorizontalAlignment = (i % 2 == 0) ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = (i < 2) ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = new Thickness(1),
            });
        }
        return g;
    }

    private static UIElement StrokePath(string pathData)
    {
        var path = new Path
        {
            Data = Geometry.Parse(pathData),
            Stroke = Brushes.White,
            StrokeThickness = StrokeW,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
        };
        return path;
    }
}
