using System.Windows.Controls;
using System.Windows.Media;

namespace WeCapture.Overlay;

/// <summary>鼠标旁的取色/坐标提示：(x, y) + RGB。</summary>
public partial class CursorTip : UserControl
{
    public CursorTip()
    {
        InitializeComponent();
    }

    public void Update(int gx, int gy, Color color)
    {
        PosText.Text = $"({gx}, {gy})";
        Swatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        ColorText.Text = $"RGB({color.R},{color.G},{color.B})";
    }
}
