using System.IO;
using System.Windows.Media.Imaging;
using WeCapture.Core;

namespace WeCapture.Export;

public static class SaveHelper
{
    /// <summary>弹出保存对话框并写文件。返回是否成功保存。</summary>
    public static bool SaveImage(BitmapSource image, AppSettings settings)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存截图",
            Filter = "PNG 图片|*.png|BMP 图片|*.bmp|JPEG 图片|*.jpg",
            FilterIndex = 1,
            FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png",
        };

        string? dir = settings.LastSaveDir;
        if (dir != null && Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        else
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        if (dlg.ShowDialog() != true)
            return false;

        BitmapEncoder encoder = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".bmp" => new BmpBitmapEncoder(),
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            _ => new PngBitmapEncoder(),
        };
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var fs = File.Create(dlg.FileName);
        encoder.Save(fs);

        settings.LastSaveDir = Path.GetDirectoryName(dlg.FileName);
        settings.Save();
        return true;
    }
}
