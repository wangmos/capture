using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WeCapture.LongShot;

/// <summary>
/// 长截图进行中的小提示窗（代码构建，无 XAML）。
/// 覆盖层此时已隐藏，必须由它来显示进度并提供取消入口。
/// </summary>
internal sealed class LongShotProgressWindow : Window
{
    private readonly TextBlock _text;

    public event Action? Cancelled;

    public LongShotProgressWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var res = Application.Current.Resources;

        _text = new TextBlock
        {
            Text = "长截图进行中…",
            Foreground = (Brush)res["TextPrimaryBrush"],
            FontFamily = (FontFamily)res["UiFont"],
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 140,
        };

        // 转动的小圆点，表明还在推进（滚动期间覆盖层是隐藏的，用户只能看到这个窗）
        var spinner = new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9,
            Fill = (Brush)res["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        spinner.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            1.0, 0.25, new Duration(TimeSpan.FromMilliseconds(650)))
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        });

        var cancel = new Button
        {
            Content = "取消",
            Style = (Style)res["SecondaryButton"],
            Height = 28,
            MinWidth = 64,
            Margin = new Thickness(14, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        cancel.Click += (_, _) => Cancelled?.Invoke();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(spinner);
        row.Children.Add(_text);
        row.Children.Add(cancel);

        Content = new Border
        {
            Background = (Brush)res["TipBrush"],
            BorderBrush = (Brush)res["TipBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 12, 10),
            Child = row,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Cancelled?.Invoke();
        };
    }

    public void Report(string message) => _text.Text = message;

    /// <summary>放在区域上方；上方放不下则放到下方。</summary>
    public void PlaceNear(Core.RectI region, double dpiScale)
    {
        UpdateLayout();
        double w = ActualWidth > 0 ? ActualWidth : 220;
        double h = ActualHeight > 0 ? ActualHeight : 44;

        double x = (region.X + region.W / 2.0) / dpiScale - w / 2;
        double y = (region.Y - 12) / dpiScale - h;
        if (y < 0) y = (region.Bottom + 12) / dpiScale;

        Left = x;
        Top = y;
    }
}
