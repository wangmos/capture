# ONNX Runtime OCR wiring test: capture a region, click OCR, assert the pipeline logs.
# Pure ASCII (PowerShell 5.1 reads .ps1 as ANSI). Self-launches --capture.
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimOcr {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
  public static void Down(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = LEFTDOWN; SendInput(1, new[]{i}, Marshal.SizeOf(typeof(INPUT)));
  }
  public static void Up() {
    var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Marshal.SizeOf(typeof(INPUT)));
  }
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(120); }
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

function TailLines([string]$pat) {
  try {
    $lines = @(Get-Content $log -Encoding UTF8)
    if ($lines.Count -lt $base) { $base = 0 }   # log was rotated since we snapshotted
    if ($lines.Count -le $base) { return @() }
    return @($lines[$base..($lines.Count-1)] | Where-Object { $_ -match $pat })
  } catch { return @() }
}

$ready = $false
for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 100; if ((TailLines 'OverlayWindow shown').Count -gt 0) { $ready = $true; break } }
if (-not $ready) { Write-Output 'OVERLAY_TIMEOUT'; exit 1 }
Start-Sleep -Milliseconds 300

# select a region that contains on-screen text, then hit the OCR button
# toolbar coords for sel (900,400)-(1600,900) scale 1, collapsed row y=928: undo 1413, ocr 1443
[SimOcr]::Drag(900, 400, 1600, 900, 15)
Start-Sleep -Milliseconds 300
[SimOcr]::Click(1443, 928)

$done = @()
for ($i = 0; $i -lt 300; $i++) {
  Start-Sleep -Milliseconds 100
  $done = TailLines 'OCR done lines=|OCR single-line'
  if ($done.Count -gt 0) { break }
  if ((TailLines 'OCR failed').Count -gt 0) { break }
}

$loaded = TailLines 'OCR model loaded'
$charset = TailLines 'OCR charset'
$failed = TailLines 'OCR failed'

Write-Output ('MODELS_LOADED=' + $loaded.Count)
foreach ($l in $loaded) { Write-Output ('  ' + $l.Substring([Math]::Min(13, $l.Length))) }
foreach ($l in $charset) { Write-Output ('  ' + $l.Substring([Math]::Min(13, $l.Length))) }
if ($failed.Count -gt 0) { foreach ($l in $failed) { Write-Output ('  FAIL ' + $l.Substring([Math]::Min(13, $l.Length))) } }

if ($done.Count -gt 0) {
  $line = $done[0]
  $line | Set-Content -Path 'C:\temp\t_ocr_onnx.txt' -Encoding UTF8
  # report only counts + timing, not the recognized screen text
  if ($line -match '(OCR (?:done|single-line).*?in \d+ms)') {
    Write-Output ('OCR_OK ' + $Matches[1])
  } else {
    Write-Output 'OCR_OK (header parse failed)'
  }
} else {
  Write-Output 'OCR_TIMEOUT'
}

Start-Sleep -Milliseconds 300
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
