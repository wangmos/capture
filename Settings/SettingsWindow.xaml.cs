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
        Icon = Core.IconFactory.WpfIcon;
        _settings = settings;
        _hotkeys = hotkeys;
        _tray = tray;
        _startCapture = startCapture;

        HotkeyBox.SetValue(settings.Hotkey);
        AutoStartBox.IsChecked = settings.AutoStart;
        AutoTextSelectBox.IsChecked = settings.AutoTextSelect;

        _shortcuts = ShortcutMap.FromDictionary(settings.Shortcuts);
        BuildShortcutRows();

        Loaded += (_, _) => LogRects();
        ContentRendered += (_, _) =>
            Dispatcher.BeginInvoke(new Action(LogRects),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // Show() 模式下 IsDefault 不生效：回车 = 保存，Esc = 取消（热键捕获期除外）
        PreviewKeyDown += (_, e) =>
        {
            // Preview 事件从窗口向下隧道，会先于绑定框拿到按键：
            // 录制快捷键期间必须让路，否则按 Esc/Enter 会直接关窗或保存
            if (HotkeyBox.IsCapturing) return;
            if (System.Windows.Input.Keyboard.FocusedElement is ShortcutBox) return;

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

    private ShortcutMap _shortcuts = ShortcutMap.CreateDefault();
    private readonly Dictionary<ShortcutAction, ShortcutBox> _shortcutBoxes = new();

    /// <summary>按目录生成"动作 + 绑定框"的行；重建时清空重来。</summary>
    private void BuildShortcutRows()
    {
        ShortcutList.Children.Clear();
        _shortcutBoxes.Clear();

        foreach (var (action, name) in ShortcutMap.Catalog)
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(8, 3, 8, 3) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = name,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(label);

            var box = new ShortcutBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                Width = 148,
                Height = 30,
                FontSize = 12,
            };
            box.SetValue(_shortcuts.Get(action));
            box.Changed += b =>
            {
                // Set 内部会解除与其他动作的冲突，重刷所有行才能反映出来
                _shortcuts.Set(action, b.Value);
                RefreshShortcutBoxes();
            };
            System.Windows.Controls.Grid.SetColumn(box, 1);
            row.Children.Add(box);

            _shortcutBoxes[action] = box;
            ShortcutList.Children.Add(row);
        }
    }

    private void RefreshShortcutBoxes()
    {
        foreach (var (action, box) in _shortcutBoxes)
            box.SetValue(_shortcuts.Get(action));
    }

    private void OnResetShortcuts(object sender, RoutedEventArgs e)
    {
        _shortcuts = ShortcutMap.CreateDefault();
        RefreshShortcutBoxes();
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
        _settings.Shortcuts = _shortcuts.ToDictionary();
        AutoStart.Apply(_settings.AutoStart);

        _settings.Save();
        Core.TraceLog.Log($"Settings applied autostart={_settings.AutoStart}");
        Close();
    }
}
