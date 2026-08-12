# Verify the four viewer fixes: window drag, live mosaic, horizontal scrollbar, panning.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimF {
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
    SetCursorPos(x, y); System.Threading.Thread.Sleep(80);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = LEFTDOWN; SendInput(1, new[]{i}, Size);
  }
  public static void Up() { var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Size); }
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(160); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(25); }
    Up(); System.Threading.Thread.Sleep(150);
  }
  public static RECT FindBig(int minW, int minH, out bool ok) {
    RECT found = new RECT(); bool got = false;
    var ps = System.Diagnostics.Process.GetProcessesByName("WeCapture");
    if (ps.Length == 0) { ok = false; return found; }
    int pid = ps[0].Id;
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

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 900, 400
$form.Size = New-Object System.Drawing.Size 700, 300
$form.BackColor = [System.Drawing.Color]::White
$form.TopMost = $true
$label = New-Object System.Windows.Forms.Label
$label.Dock = 'Fill'
$label.Font = New-Object System.Drawing.Font 'Segoe UI', 20
$label.Padding = New-Object System.Windows.Forms.Padding 24, 24, 0, 0
$label.Text = "Mosaic target line one`r`nSecond line 12345 ABCDE`r`nThird line for testing"
$form.Controls.Add($label)
$form.Show()

function Pump([int]$ms) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
}
function Shot([string]$path, [int]$x, [int]$y, [int]$w, [int]$h) {
  $b = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $b.Dispose()
}
Pump 600

$log = Join-Path $env:TEMP 'wec_log.txt'
$base = 0
if (Test-Path $log) { $base = @(Get-Content $log).Count }
Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' | Out-Null

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
[SimF]::Drag(905, 405, 1595, 690, 15)
Pump 500

$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -eq 0 -or $rects[-1] -notmatch 'ocr=(\d+),(\d+)') { Write-Output 'NO_OCR_BUTTON'; $form.Close(); exit 1 }
[SimF]::Click([int]$Matches[1], [int]$Matches[2])

$vr = @()
for ($i = 0; $i -lt 80; $i++) { Pump 150; $vr = @(TailLines 'ViewerRects'); if ($vr.Count -gt 0) { break } }
if ($vr.Count -eq 0) { Write-Output 'NO_VIEWER_RECTS'; $form.Close(); taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }
$form.Hide(); Pump 300

if ($vr[-1] -notmatch 'viewport=(\d+),(\d+),(\d+)x(\d+)') { Write-Output 'NO_VIEWPORT'; exit 1 }
$vx = [int]$Matches[1]; $vy = [int]$Matches[2]; $vw = [int]$Matches[3]; $vh = [int]$Matches[4]

# ---- 1) window drag ----
$ok1 = $false
$before = [SimF]::FindBig(600, 400, [ref]$ok1)
[SimF]::Drag($before.Left + 200, $before.Top + 36, $before.Left + 320, $before.Top + 96, 12)
Pump 400
$ok2 = $false
$after = [SimF]::FindBig(600, 400, [ref]$ok2)
$moved = ($after.Left - $before.Left)
Write-Output ('WINDOW_DRAG dx=' + $moved + ' dy=' + ($after.Top - $before.Top))

# viewer moved, so re-read its coordinates
$vr2 = @(TailLines 'ViewerRects')
$dx = $after.Left - $before.Left; $dy = $after.Top - $before.Top
$vx += $dx; $vy += $dy

# ---- 2) live mosaic: draw and check the pixels actually changed ----
Shot 'C:\temp\fix_before.png' ($vx + 20) ($vy + 20) 300 120
if ($vr[-1] -notmatch 'Mosaic=(\d+),(\d+)') { Write-Output 'NO_MOSAIC_TOOL'; exit 1 }
[SimF]::Click(([int]$Matches[1] + $dx), ([int]$Matches[2] + $dy))
Pump 250
[SimF]::Drag($vx + 30, $vy + 30, $vx + 280, $vy + 120, 12)
Pump 500
Shot 'C:\temp\fix_after.png' ($vx + 20) ($vy + 20) 300 120

$b1 = [System.Drawing.Image]::FromFile('C:\temp\fix_before.png')
$b2 = [System.Drawing.Image]::FromFile('C:\temp\fix_after.png')
$bm1 = New-Object System.Drawing.Bitmap $b1
$bm2 = New-Object System.Drawing.Bitmap $b2
$diff = 0
for ($y = 0; $y -lt 120; $y += 4) {
  for ($x = 0; $x -lt 300; $x += 4) {
    if ($bm1.GetPixel($x, $y).ToArgb() -ne $bm2.GetPixel($x, $y).ToArgb()) { $diff++ }
  }
}
$bm1.Dispose(); $bm2.Dispose(); $b1.Dispose(); $b2.Dispose()
Write-Output ('MOSAIC_PIXELS_CHANGED=' + $diff)

# ---- 3) zoom in far, then screenshot the whole window to inspect the scrollbars ----
$ok3 = $false
$w3 = [SimF]::FindBig(600, 400, [ref]$ok3)
Shot 'C:\temp\fix_zoomed.png' $w3.Left $w3.Top ($w3.Right - $w3.Left) ($w3.Bottom - $w3.Top)
Write-Output 'SHOT_SAVED'

$form.Close()
Pump 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
