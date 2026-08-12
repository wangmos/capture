namespace WeCapture.Core;

/// <summary>
/// 单实例守卫：Mutex 判定首实例；EventWaitHandle 让第二实例向首实例发信号
/// （普通激活 / 直接开始截图），随后自行退出。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\WeCapture.Mutex";
    private const string EventActivate = @"Local\WeCapture.Activate";
    private const string EventCapture = @"Local\WeCapture.Capture";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _evtActivate;
    private readonly EventWaitHandle _evtCapture;
    private readonly Thread _listener;
    private readonly CancellationTokenSource _cts = new();

    public bool IsFirstInstance { get; }

    /// <summary>第二实例请求截图时触发（首实例）。</summary>
    public event Action? CaptureRequested;

    private SingleInstance(Mutex mutex, EventWaitHandle evtActivate, EventWaitHandle evtCapture, bool isFirst)
    {
        _mutex = mutex;
        _evtActivate = evtActivate;
        _evtCapture = evtCapture;
        IsFirstInstance = isFirst;
        _listener = new Thread(ListenLoop) { IsBackground = true, Name = "SingleInstanceListener" };
    }

    /// <summary>
    /// 尝试获取单实例。isFirst=false 时返回的对象仅用于发出信号后即退出。
    /// </summary>
    public static SingleInstance Acquire(bool requestCapture)
    {
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        var evtActivate = new EventWaitHandle(false, EventResetMode.AutoReset, EventActivate);
        var evtCapture = new EventWaitHandle(false, EventResetMode.AutoReset, EventCapture);

        if (!createdNew)
        {
            // 第二实例：通知首实例后退出
            if (requestCapture)
                evtCapture.Set();
            else
                evtActivate.Set();
        }

        var si = new SingleInstance(mutex, evtActivate, evtCapture, createdNew);
        if (createdNew)
            si._listener.Start();
        return si;
    }

    private void ListenLoop()
    {
        var handles = new[] { _evtActivate, _evtCapture };
        while (!_cts.IsCancellationRequested)
        {
            int idx;
            try
            {
                idx = WaitHandle.WaitAny(handles, 500);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (idx == 1)
                CaptureRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Join(1000); } catch { /* 忽略 */ }
        _mutex.Dispose();
        _evtActivate.Dispose();
        _evtCapture.Dispose();
    }
}
