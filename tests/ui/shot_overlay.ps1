# Screenshot the live overlay (mask + selection + auto text boxes) for visual review.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimS {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
  static int Size { get { return Marshal.SizeOf(typeof(INPUT)); } }
  public static void Down(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = LEFTDOWN; SendInput(1, new[]{i}, Size);
  }
  public static void Up() { var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Size); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(20); }
    Up(); System.Threading.Thread.Sleep(120);
  }
}
"@

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 900, 400
$form.Size = New-Object System.Drawing.Size 700, 400
$form.BackColor = [System.Drawing.Color]::White
$form.TopMost = $true
$label = New-Object System.Windows.Forms.Label
$label.Dock = 'Fill'
$label.Font = New-Object System.Drawing.Font 'Segoe UI', 18
$label.Padding = New-Object System.Windows.Forms.Padding 24, 24, 0, 0
$label.Text = "Auto text detection box test`r`nSecond line 12345 ABCDE`r`nThird line for review`r`nFourth line here"
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

# select part of the form so both the mask and the selection are visible
[SimS]::Drag(930, 430, 1520, 700, 15)
for ($i = 0; $i -lt 100; $i++) { Pump 120; if ((TailLines 'TextLayer ready').Count -gt 0) { break } }
Pump 600

# move the cursor away WITHOUT clicking: a click outside the selection would
# expand it and restart the recognition
[SimS]::SetCursorPos(1200, 1000)
Pump 500

$x = 860; $y = 360; $w = 800; $h = 620
$b = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$b.Save('C:\temp\ui_overlay.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $b.Dispose()
Write-Output 'OVERLAY_SHOT_SAVED'

$form.Close()
Pump 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
