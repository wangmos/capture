using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace WeCapture.Export;

public static class ClipboardHelper
{
    /// <summary>写入剪贴板，被占用时重试 3 次。</summary>
    public static bool SetImage(BitmapSource image)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(100);
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return false;
    }
}
