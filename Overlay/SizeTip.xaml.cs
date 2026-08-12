using System.Windows.Controls;

namespace WeCapture.Overlay;

/// <summary>拖选/调整时的“宽 × 高”尺寸提示。</summary>
public partial class SizeTip : UserControl
{
    public SizeTip()
    {
        InitializeComponent();
    }

    public void Update(int w, int h)
    {
        SizeText.Text = $"{w} × {h}";
    }
}
