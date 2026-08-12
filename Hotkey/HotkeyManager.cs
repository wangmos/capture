using System.Windows.Interop;
using WeCapture.Core;
using WeCapture.Native;

namespace WeCapture.Hotkey;

/// <summary>全局热键管理：RegisterHotKey 挂在 HWND_MESSAGE 消息窗口上，HwndSource 收 WM_HOTKEY。</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xC1A0;

    private readonly HwndSource _source;
    private HotkeyDef? _current;
    private Action? _callback;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("WeCaptureHotkeyHost")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>注册热键。失败（被其他程序占用）返回 false。</summary>
    public bool TryRegister(HotkeyDef def, Action callback)
    {
        Unregister();
        _callback = callback;

        bool ok = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, def.ModifiersWin32(), def.VirtualKey());
        if (ok)
            _current = def.Clone();
        else
            _callback = null;
        return ok;
    }

    public void Unregister()
    {
        if (_current != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _current = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt64() == HotkeyId)
        {
            _callback?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
