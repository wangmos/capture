# Trigger the OCR result window on known text and screenshot it for design review.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class SimO {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  public delegate bool Proc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Proc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
  static int Size { get { return Marshal.SizeOf(typeof(INPUT)); } }
  public static void Down(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = LEFTDOWN; SendInput(1, new[]{i}, Size);
  }
  public static void Up() { var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Size); }
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(120); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(20); }
    Up(); System.Threading.Thread.Sleep(80);
  }
  public static RECT FindBig(int pid, int minW, int minH, out bool ok) {
    RECT found = new RECT(); bool got = false;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      int wp; GetWindowThreadProcessId(h, out wp);
      if (wp != pid || !IsWindowVisible(h)) return true;
      RECT r; if (!GetWindowRect(h, out r)) return true;
      if (r.Right - r.Left < minW || r.Bottom - r.Top < minH) return true;
      found = r; got = true; return false;
    }, IntPtr.Zero);
    ok = got; return found;
  }
}
"@

$expected = @('WeCapture OCR result sample', 'Hello ONNX Runtime 1.29', 'Third line 12345 ABCDE')
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 900, 400
$form.Size = New-Object System.Drawing.Size 700, 300
$form.BackColor = [System.Drawing.Color]::White
$form.TopMost = $true
$label = New-Object System.Windows.Forms.Label
$label.Dock = 'Fill'
$label.Font = New-Object System.Drawing.Font 'Microsoft YaHei UI', 20
$label.Padding = New-Object System.Windows.Forms.Padding 24, 24, 0, 0
$label.Text = ($expected -join "`r`n")
$form.Controls.Add($label)
$form.Show()

function Pump([int]$ms) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
}
Pump 600

$log = Join-Path $env:TEMP 'wec_log.txt'
$base = 0
if (Test-Path $log) { $base = @(Get-Content $log).Count }
$proc = Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' -PassThru

function TailLines([string]$pat) {
  try {
    $l = @(Get-Content $log -Encoding UTF8)
    if ($l.Count -lt $base) { $base = 0 }   # log was rotated since we snapshotted
    if ($l.Count -le $base) { return @() }
    return @($l[$base..($l.Count-1)] | Where-Object { $_ -match $pat })
  } catch { return @() }
}

for ($i = 0; $i -lt 60; $i++) { Pump 100; if ((TailLines 'OverlayWindow shown').Count -gt 0) { break } }
Pump 300

[SimO]::Drag(905, 405, 1595, 690, 15)
Pump 500

$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -eq 0 -or $rects[-1] -notmatch 'ocr=(\d+),(\d+)') { Write-Output 'NO_OCR_BUTTON'; $form.Close(); exit 1 }
[SimO]::Click([int]$Matches[1], [int]$Matches[2])

for ($i = 0; $i -lt 100; $i++) { Pump 100; if ((TailLines 'OCR done').Count -gt 0) { break } }
Pump 900

# wait for the viewer + docked text window, then capture the whole screen
$ok = $false
for ($t = 0; $t -lt 40; $t++) {
  $r = [SimO]::FindBig($proc.Id, 600, 400, [ref]$ok)
  if ($ok) { break }
  Pump 200
}
if (-not $ok) { Write-Output 'VIEWER_NOT_FOUND' } else { Write-Output ('VIEWER ' + ($r.Right-$r.Left) + 'x' + ($r.Bottom-$r.Top)) }
Pump 1500
$form.Hide(); Pump 300
$sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $sb.Width, $sb.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($sb.Location, [System.Drawing.Point]::Empty, $sb.Size)
$bmp.Save('C:\temp\ui_ocr.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output 'SAVED fullscreen'

$form.Close()
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
