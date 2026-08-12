using System.Windows.Forms;
using WeCapture.Core;
using WeCapture.Native;

namespace WeCapture.Tray;

/// <summary>系统托盘宿主：NotifyIcon + 右键菜单 + 双击截图 + explorer 重启后重建图标。</summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly TaskbarMessageWindow _taskbarWatcher;
    private readonly ToolStripMenuItem _captureItem;

    public event Action? CaptureRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayHost(string hotkeyText)
    {
        _menu = new ContextMenuStrip();
        _captureItem = new ToolStripMenuItem($"开始截图（{hotkeyText}）") { Tag = "capture" };
        _captureItem.Click += (_, _) => CaptureRequested?.Invoke();

        var settingsItem = new ToolStripMenuItem("设置") { Tag = "settings" };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("退出") { Tag = "exit" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.Add(_captureItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.TrayIcon,
            Text = "WeCapture - 微信截图风格工具",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => CaptureRequested?.Invoke();

        _taskbarWatcher = new TaskbarMessageWindow(this);
    }

    public void UpdateHotkeyText(string hotkeyText)
    {
        _captureItem.Text = $"开始截图（{hotkeyText}）";
    }

    /// <summary>explorer.exe 重启后任务栏重建，托盘图标会丢失，需要重新创建。</summary>
    internal void RecreateIcon()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        _taskbarWatcher.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    /// <summary>隐藏窗口，用于接收 TaskbarCreated 广播消息。</summary>
    private sealed class TaskbarMessageWindow : NativeWindow, IDisposable
    {
        private readonly TrayHost _host;
        private readonly uint _taskbarCreatedMsg;

        public TaskbarMessageWindow(TrayHost host)
        {
            _host = host;
            _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == _taskbarCreatedMsg)
                _host.RecreateIcon();
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}
