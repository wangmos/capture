using System.Windows;
using WeCapture.Core;
using WeCapture.Hotkey;
using WeCapture.Tray;

namespace WeCapture.Settings;

/// <summary>设置窗口：热键（即时生效）/开机自启。</summary>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    private readonly AppSettings _settings;
    private readonly HotkeyManager _hotkeys;
    private readonly TrayHost _tray;
    private readonly Action _startCapture;

    public static void ShowSingleton(AppSettings settings, HotkeyManager hotkeys, TrayHost tray, Action startCapture)
    {
        if (_instance is { } w)
        {
            w.Activate();
            return;
        }
        _instance = new SettingsWindow(settings, hotkeys, tray, startCapture);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    private SettingsWindow(AppSettings settings, HotkeyManager hotkeys, TrayHost tray, Action startCapture)
    {
        InitializeComponent();
        _settings = settings;
        _hotkeys = hotkeys;
        _tray = tray;
        _startCapture = startCapture;

        HotkeyBox.SetValue(settings.Hotkey);
        AutoStartBox.IsChecked = settings.AutoStart;
        AutoTextSelectBox.IsChecked = settings.AutoTextSelect;

        Loaded += (_, _) => LogRects();
        ContentRendered += (_, _) =>
            Dispatcher.BeginInvoke(new Action(LogRects),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // Show() 模式下 IsDefault 不生效：回车 = 保存，Esc = 取消（热键捕获期除外）
        PreviewKeyDown += (_, e) =>
        {
            if (HotkeyBox.IsCapturing) return;

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                OnOk(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    /// <summary>自绘标题栏：按住拖动窗口。</summary>
    private void OnTitleBarDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void LogRects()
    {
        var hb = HotkeyBox.PointToScreen(new System.Windows.Point(0, 0));
        var ok = OkButton.PointToScreen(new System.Windows.Point(0, 0));
        var auto = AutoStartBox.PointToScreen(new System.Windows.Point(0, 0));
        Core.TraceLog.Log(
            $"SettingsWindow shown hotkeyBox=({(int)hb.X},{(int)hb.Y},{(int)HotkeyBox.ActualWidth},{(int)HotkeyBox.ActualHeight}) " +
            $"ok=({(int)ok.X},{(int)ok.Y},{(int)OkButton.ActualWidth},{(int)OkButton.ActualHeight}) " +
            $"auto=({(int)auto.X},{(int)auto.Y},{(int)AutoStartBox.ActualWidth},{(int)AutoStartBox.ActualHeight})");
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var hk = HotkeyBox.Value;
        if (hk.ToString() != _settings.Hotkey.ToString())
        {
            var old = _settings.Hotkey;
            if (!_hotkeys.TryRegister(hk, _startCapture))
            {
                _hotkeys.TryRegister(old, _startCapture); // 失败回退旧热键
                MessageBox.Show($"热键 {hk} 已被其他程序占用，保留原热键。", "WeCapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.Hotkey = hk;
            _tray.UpdateHotkeyText(hk.ToString());
            Core.TraceLog.Log($"Settings applied hotkey={hk}");
        }

        _settings.AutoStart = AutoStartBox.IsChecked == true;
        _settings.AutoTextSelect = AutoTextSelectBox.IsChecked == true;
        AutoStart.Apply(_settings.AutoStart);

        _settings.Save();
        Core.TraceLog.Log($"Settings applied autostart={_settings.AutoStart}");
        Close();
    }
}
