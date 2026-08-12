using System.IO;

namespace WeCapture.Core;

/// <summary>极简文件日志（托盘应用无控制台，排障用）。</summary>
public static class TraceLog
{
    private static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "wec_log.txt");

    private static readonly object Lock = new();

    /// <summary>超过此大小就从头写起（日志只增不减会一直长下去）。</summary>
    private const long MaxBytes = 1024 * 1024;

    private static bool _rolled;

    public static void Log(string msg)
    {
        try
        {
            lock (Lock)
            {
                RollIfTooLargeLocked();
                File.AppendAllText(Path,
                    $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 每个进程只检查一次。测试脚本以行号为基线读取本文件，
    /// 所以只能在进程启动时轮转，不能在运行中途截断。
    /// </summary>
    private static void RollIfTooLargeLocked()
    {
        if (_rolled) return;
        _rolled = true;

        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists || info.Length <= MaxBytes) return;

            string old = Path + ".old";
            File.Delete(old);
            File.Move(Path, old);
        }
        catch
        {
            // 轮转失败无所谓，继续往原文件写
        }
    }
}
