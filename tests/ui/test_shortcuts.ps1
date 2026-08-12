# Verify the toolbar shortcuts: single letters switch tools, Ctrl combos fire actions.
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimK {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, KEYUP = 0x0002;
  static int Size { get { return Marshal.SizeOf(typeof(INPUT)); } }
  public static void Down(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = LEFTDOWN; SendInput(1, new[]{i}, Size);
  }
  public static void Up() { var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Size); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(18); }
    Up(); System.Threading.Thread.Sleep(80);
  }
  public static void Key(ushort vk, bool up) {
    var i = new INPUT(); i.type = 1; i.U.ki.wVk = vk; i.U.ki.dwFlags = up ? KEYUP : 0;
    SendInput(1, new[]{i}, Size);
  }
  public static void Tap(ushort vk) { Key(vk, false); System.Threading.Thread.Sleep(35); Key(vk, true); System.Threading.Thread.Sleep(160); }
  public static void Ctrl(ushort vk) {
    Key(0x11, false); System.Threading.Thread.Sleep(30);
    Key(vk, false); System.Threading.Thread.Sleep(40); Key(vk, true); System.Threading.Thread.Sleep(30);
    Key(0x11, true); System.Threading.Thread.Sleep(200);
  }
}
"@

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

$ready = $false
for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 100; if ((TailLines 'OverlayWindow shown').Count -gt 0) { $ready = $true; break } }
if (-not $ready) { Write-Output 'OVERLAY_TIMEOUT'; exit 1 }
Start-Sleep -Milliseconds 300

[SimK]::Drag(900, 400, 1600, 900, 15)
Start-Sleep -Milliseconds 500

# single letters -> tools.  R rect, O ellipse, A arrow, P pen, T text, M mosaic, N number, I text-select
$map = @{ 'R' = 0x52; 'O' = 0x4F; 'A' = 0x41; 'M' = 0x4D; 'N' = 0x4E; 'I' = 0x49 }
$expect = @{ 'R' = 'Rectangle'; 'O' = 'Ellipse'; 'A' = 'Arrow'; 'M' = 'Mosaic'; 'N' = 'Number'; 'I' = 'TextSelect' }
$pass = 0; $total = 0
foreach ($k in 'R','O','A','M','N','I') {
  [SimK]::Tap([uint16]$map[$k])
  Start-Sleep -Milliseconds 180
  $hits = @(TailLines ('SetTool ' + $expect[$k] + ' active=' + $expect[$k]))
  $total++
  if ($hits.Count -gt 0) { $pass++ } else { Write-Output ('MISS key ' + $k + ' -> ' + $expect[$k]) }
}
Write-Output ('TOOL_SHORTCUTS=' + $pass + '/' + $total)

# Ctrl+E -> OCR (recognize all).
# The auto text-select already logged one 'OCR done', so require a NEW one.
$before = @(TailLines 'OCR done|OCR single-line').Count
[SimK]::Ctrl(0x45)
$after = $before
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 100
  $after = @(TailLines 'OCR done|OCR single-line').Count
  if ($after -gt $before) { break }
}
$ocrFired = $after -gt $before
Write-Output ('CTRL_E_OCR=' + $ocrFired + ' (before=' + $before + ' after=' + $after + ')')

if ($pass -eq $total -and $ocrFired) { Write-Output 'SHORTCUTS_OK' } else { Write-Output 'SHORTCUTS_FAIL' }

Start-Sleep -Milliseconds 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
