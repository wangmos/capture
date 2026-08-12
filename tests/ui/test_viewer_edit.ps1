# Viewer test: capture -> OCR button opens the shared viewer with a docked text window,
# then draw a rectangle annotation inside the viewer and copy the flattened image.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimV {
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
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(150); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(20); }
    Up(); System.Threading.Thread.Sleep(120);
  }
}
"@

$expected = @('Viewer edit test line one', 'Second line 12345')
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
Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' | Out-Null
try { [System.Windows.Forms.Clipboard]::Clear() } catch { }

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

[SimV]::Drag(905, 405, 1595, 690, 15)
Pump 500

$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -eq 0 -or $rects[-1] -notmatch 'ocr=(\d+),(\d+)') { Write-Output 'NO_OCR_BUTTON'; $form.Close(); exit 1 }
[SimV]::Click([int]$Matches[1], [int]$Matches[2])

# the viewer logs its own button coordinates
$vr = @()
for ($i = 0; $i -lt 80; $i++) { Pump 150; $vr = @(TailLines 'ViewerRects'); if ($vr.Count -gt 0) { break } }
if ($vr.Count -eq 0) { Write-Output 'NO_VIEWER_RECTS'; $form.Close(); taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }
Write-Output 'VIEWER_OPENED'

if ($vr[-1] -notmatch 'Rectangle=(\d+),(\d+)') { Write-Output 'NO_RECT_TOOL'; exit 1 }
$rx = [int]$Matches[1]; $ry = [int]$Matches[2]
if ($vr[-1] -notmatch 'viewport=(\d+),(\d+),(\d+)x(\d+)') { Write-Output 'NO_VIEWPORT'; exit 1 }
$vx = [int]$Matches[1]; $vy = [int]$Matches[2]; $vw = [int]$Matches[3]; $vh = [int]$Matches[4]
Write-Output ("VIEWPORT=" + $vw + "x" + $vh)

# pick the rectangle tool, then drag inside the viewport
[SimV]::Click($rx, $ry)
Pump 300
$tool = @(TailLines 'Viewer SetTool Rectangle active=Rectangle')
Write-Output ('TOOL_SELECTED=' + ($tool.Count -gt 0))

[SimV]::Drag($vx + 60, $vy + 60, $vx + [int]($vw * 0.6), $vy + [int]($vh * 0.6), 12)
Pump 400

# OCR text window must be docked next to the viewer
$ocr = @(TailLines 'OCR done|OCR single-line')
Write-Output ('OCR_RAN=' + ($ocr.Count -gt 0))

# copy: click the viewer's own copy button (Ctrl+C would go to the focused text window)
$clip = $null
if ($vr[-1] -notmatch 'copy=(\d+),(\d+)') { Write-Output 'NO_COPY_BUTTON'; $form.Close(); exit 1 }
[SimV]::Click([int]$Matches[1], [int]$Matches[2])
Pump 900
try { $clip = [System.Windows.Forms.Clipboard]::GetImage() } catch { }
if ($clip -ne $null) { Write-Output ('CLIPBOARD_IMAGE=' + $clip.Width + 'x' + $clip.Height) }
else { Write-Output 'CLIPBOARD_IMAGE=none' }

if ($tool.Count -gt 0 -and $ocr.Count -gt 0 -and $clip -ne $null) { Write-Output 'VIEWER_EDIT_OK' }
else { Write-Output 'VIEWER_EDIT_FAIL' }

$form.Close()
Pump 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
