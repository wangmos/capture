using System.IO;

namespace WeCapture.Core;

/// <summary>极简文件日志（托盘应用无控制台，排障用）。</summary>
public static class TraceLog
{
    private static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "wec_log.txt");

    private static readonly object Lock = new();

    public static void Log(string msg)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(Path,
                    $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
        }
        catch
        {
        }
    }
}
