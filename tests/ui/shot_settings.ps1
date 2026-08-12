# Screenshot the settings window so the design can be reviewed visually.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Shot {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool Proc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Proc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
  public static RECT Find(int pid, out bool ok) {
    RECT found = new RECT(); bool got = false;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      int wp; GetWindowThreadProcessId(h, out wp);
      if (wp != pid || !IsWindowVisible(h)) return true;
      RECT r; if (!GetWindowRect(h, out r)) return true;
      if (r.Right - r.Left < 200 || r.Bottom - r.Top < 120) return true;
      found = r; got = true; return false;
    }, IntPtr.Zero);
    ok = got; return found;
  }
}
"@

taskkill /F /IM WeCapture.exe 2>$null | Out-Null
Start-Sleep -Milliseconds 400
$p = Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--settings' -PassThru
Start-Sleep -Milliseconds 2500

$ok = $false
$r = [Shot]::Find($p.Id, [ref]$ok)
if (-not $ok) { Write-Output 'WINDOW_NOT_FOUND'; exit 1 }

$pad = 8
$x = [Math]::Max(0, $r.Left - $pad); $y = [Math]::Max(0, $r.Top - $pad)
$w = ($r.Right - $r.Left) + $pad * 2; $h = ($r.Bottom - $r.Top) + $pad * 2
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save('C:\temp\ui_settings.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output ("SAVED {0}x{1}" -f $w, $h)
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
