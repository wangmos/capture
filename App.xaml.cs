using System.Windows;
using WeCapture.Core;
using WeCapture.Hotkey;
using WeCapture.Session;
using WeCapture.Tray;

namespace WeCapture;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private TrayHost? _tray;
    private HotkeyManager? _hotkeyManager;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Core.TraceLog.Log($"OnStartup args=[{string.Join(",", e.Args)}]");

        DispatcherUnhandledException += (_, ae) =>
            Core.TraceLog.Log($"DispatcherUnhandledException: {ae.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, ae) =>
            Core.TraceLog.Log($"UnhandledException: {ae.ExceptionObject}");

        bool requestCapture = e.Args.Any(a => string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));
        bool requestSettings = e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));

        // 单实例：第二实例发信号后立即退出
        _singleInstance = SingleInstance.Acquire(requestCapture);
        if (!_singleInstance.IsFirstInstance)
        {
            Core.TraceLog.Log("second instance, exit");
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();

        _hotkeyManager = new HotkeyManager();
        if (!_hotkeyManager.TryRegister(_settings.Hotkey, StartCapture))
        {
            Core.TraceLog.Log("hotkey register failed");
            MessageBox.Show(
                $"全局热键 {_settings.Hotkey} 已被其他程序占用，截图快捷键暂不可用。\n可在“设置”中更换热键。",
                "WeCapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _tray = new TrayHost(_settings.Hotkey.ToString());
        _tray.CaptureRequested += StartCapture;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += ExitApp;

        _singleInstance.CaptureRequested += () => Dispatcher.BeginInvoke(StartCapture);

        if (requestCapture)
            StartCapture();
        if (requestSettings)
            OpenSettings();
        Core.TraceLog.Log("OnStartup done");
    }

    /// <summary>开始一次截图会话（热键/托盘/二实例信号共用入口）。</summary>
    public void StartCapture()
    {
        Dispatcher.Invoke(() =>
        {
            if (CaptureSession.IsActive) return; // 已有会话，忽略（同微信）
            CaptureSession.Start(_settings);
        });
    }

    private void OpenSettings()
    {
        Dispatcher.Invoke(() => WeCapture.Settings.SettingsWindow.ShowSingleton(_settings, _hotkeyManager!, _tray!, StartCapture));
    }

    private void ExitApp()
    {
        _hotkeyManager?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
