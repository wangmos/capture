# number tool test: badges 1..3 (3rd outside -> selection extends), undo, double-click copy
# no UIA: fixed toolbar coords + log-verified retries
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Sim31 {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
  public static void Down(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN; SendInput(1, new[]{i}, Marshal.SizeOf(typeof(INPUT)));
  }
  public static void Up() {
    var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = MOUSEEVENTF_LEFTUP; SendInput(1, new[]{u}, Marshal.SizeOf(typeof(INPUT)));
  }
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(120); }
  public static void DblClick(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(90); Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(80); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(15); }
    Up(); System.Threading.Thread.Sleep(80);
  }
  public static void Move(int x, int y) { SetCursorPos(x, y); }
}
"@

$log = Join-Path $env:TEMP 'wec_log.txt'
$base = 0
if (Test-Path $log) { $base = @(Get-Content $log).Count }

Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' | Out-Null

try { [System.Windows.Forms.Clipboard]::Clear() } catch { }

function TailLines([string]$pat) {
  try {
    $lines = @(Get-Content $log)
    if ($lines.Count -lt $base) { $base = 0 }   # log was rotated since we snapshotted
    if ($lines.Count -le $base) { return @() }
    return @($lines[$base..($lines.Count-1)] | Where-Object { $_ -match $pat })
  } catch { return @() }
}

$ready = $false
for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 100; if ((TailLines 'OverlayWindow shown').Count -gt 0) { $ready = $true; break } }
if (-not $ready) { Write-Output 'OVERLAY_TIMEOUT'; exit 1 }
Start-Sleep -Milliseconds 300

# Button coords come from the app's own 'ToolbarRects' log line (physical px).
# Never hardcode them: the toolbar is right-anchored, so adding/removing a button
# shifts every button to its LEFT.
function BtnXY([string]$name) {
  $rects = @(TailLines 'ToolbarRects')
  if ($rects.Count -eq 0) { return $null }
  if ($rects[-1] -match ($name + '=(\d+),(\d+)')) { return @([int]$Matches[1], [int]$Matches[2]) }
  return $null
}

function Shot([string]$path, [int]$x, [int]$y, [int]$w, [int]$h) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

# try: drag selection -> number tool -> click inside => expect 'Number placed idx=1'
$placed1 = @()
for ($attempt = 0; $attempt -lt 3; $attempt++) {
  [Sim31]::Drag(900, 400, 1600, 900, 15)
  Start-Sleep -Milliseconds 400
  $nb = BtnXY 'Number'
  if ($nb -eq $null) { Write-Output 'NO_TOOLBAR_RECTS'; exit 1 }
  [Sim31]::Click($nb[0], $nb[1])     # number tool toggle
  Start-Sleep -Milliseconds 250
  [Sim31]::Click(1000, 500)     # badge 1
  for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Milliseconds 100
    $placed1 = TailLines 'Number placed idx=1'
    if ($placed1.Count -gt 0) { break }
  }
  if ($placed1.Count -gt 0) { break }
}
if ($placed1.Count -eq 0) { Write-Output 'PLACE1_TIMEOUT'; exit 1 }

[Sim31]::Click(1200, 600)       # badge 2 inside
[Sim31]::Click(1700, 800)       # badge 3 OUTSIDE -> selection must extend
Start-Sleep -Milliseconds 400

[Sim31]::Move(2400, 1350)
Start-Sleep -Milliseconds 250
Shot "C:\temp\t_number.png" 860 360 940 660

$placed = TailLines 'Number placed'
Write-Output ("PLACED=" + $placed.Count)
foreach ($l in $placed) { Write-Output ("  " + $l.Substring([Math]::Min(12, $l.Length))) }

$extOk = ($placed | Where-Object { $_ -match 'idx=3' -and $_ -match 'outside=True' }).Count -gt 0
Write-Output ("EXTEND_OK=" + $extOk)

# undo: the selection was extended by badge 3, so the toolbar moved; re-read its coords
$ub = BtnXY 'undo'
if ($ub -eq $null) { Write-Output 'NO_TOOLBAR_RECTS_UNDO'; exit 1 }
Write-Output ('UNDO_BTN=' + $ub[0] + ',' + $ub[1])
[Sim31]::Click($ub[0], $ub[1])
Start-Sleep -Milliseconds 300
[Sim31]::Move(2400, 1350)
Start-Sleep -Milliseconds 250
Shot "C:\temp\t_number_undo.png" 860 360 940 660

# double-click inside extended selection -> copy; size must equal the extended selection
$expW = 0; $expH = 0
if ($placed.Count -gt 0 -and $placed[-1] -match '(\d+)x(\d+)\s*$') { $expW = [int]$Matches[1]; $expH = [int]$Matches[2] }
[Sim31]::DblClick(1250, 700)

$ok = $false
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 100
  try {
    $img = [System.Windows.Forms.Clipboard]::GetImage()
    if ($img -ne $null -and $img.Width -eq $expW -and $img.Height -eq $expH) { $ok = $true; break }
  } catch { }
}
Write-Output ("NUMBER_DONE expect=${expW}x${expH} clipboard=" + $ok)
