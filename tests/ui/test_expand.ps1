# expand selection by clicking outside: directional cursors + edge expansion + annotation kept
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Sim50 {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x; public int y; }
  [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreen; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO pci);
  [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);
  public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    var d = new INPUT(); d.type = 0; d.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN; SendInput(1, new[]{d}, Marshal.SizeOf(typeof(INPUT)));
    System.Threading.Thread.Sleep(40);
    var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = MOUSEEVENTF_LEFTUP; SendInput(1, new[]{u}, Marshal.SizeOf(typeof(INPUT)));
    System.Threading.Thread.Sleep(80);
  }
  public static void DblClick(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
    for (int i = 0; i < 2; i++) {
      var d = new INPUT(); d.type = 0; d.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN; SendInput(1, new[]{d}, Marshal.SizeOf(typeof(INPUT)));
      System.Threading.Thread.Sleep(30);
      var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = MOUSEEVENTF_LEFTUP; SendInput(1, new[]{u}, Marshal.SizeOf(typeof(INPUT)));
      System.Threading.Thread.Sleep(60);
    }
  }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    SetCursorPos(x1, y1); System.Threading.Thread.Sleep(60);
    var i = new INPUT(); i.type = 0; i.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN; SendInput(1, new[]{i}, Marshal.SizeOf(typeof(INPUT)));
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(15); }
    var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = MOUSEEVENTF_LEFTUP; SendInput(1, new[]{u}, Marshal.SizeOf(typeof(INPUT)));
    System.Threading.Thread.Sleep(80);
  }
  public static string CursorAt(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(350);
    var ci = new CURSORINFO(); ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
    if (!GetCursorInfo(ref ci)) return "ERR";
    IntPtr h = ci.hCursor;
    if (h == LoadCursor(IntPtr.Zero, new IntPtr(32644))) return "WE";
    if (h == LoadCursor(IntPtr.Zero, new IntPtr(32645))) return "NS";
    if (h == LoadCursor(IntPtr.Zero, new IntPtr(32642))) return "NWSE";
    if (h == LoadCursor(IntPtr.Zero, new IntPtr(32643))) return "NESW";
    if (h == LoadCursor(IntPtr.Zero, new IntPtr(32515))) return "CROSS";
    if (h == LoadCursor(IntPtr.Zero, new IntPtr(32649))) return "HAND";
    return "OTHER";
  }
}
"@

$exe = 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe'
$log = Join-Path $env:TEMP 'wec_log.txt'
$base = 0
if (Test-Path $log) { $base = @(Get-Content $log).Count }

Start-Process $exe -ArgumentList '--capture' | Out-Null

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

# selection (900,400)-(1600,900)
[Sim50]::Drag(900, 400, 1600, 900, 15)
Start-Sleep -Milliseconds 400

# Locate the pen button from the app's own ToolbarRects log line.
# Never hardcode/probe: the toolbar is right-anchored, so every added button shifts
# the tools left, and a wrong click opens the style panel which moves the whole row down.
$pen = $false
$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -gt 0 -and $rects[-1] -match 'Pen=(\d+),(\d+)') {
  $px = [int]$Matches[1]; $py = [int]$Matches[2]
  [Sim50]::Click($px, $py)
  Start-Sleep -Milliseconds 500
  if ((TailLines 'active=Pen').Count -gt 0) { Write-Output ("PENBTN=" + $px + "," + $py); $pen = $true }
} else { Write-Output 'NO_TOOLBAR_RECTS' }
Write-Output ("pen=" + $pen)
if (-not $pen) { Write-Output 'PEN_TOOL_FAIL'; exit 1 }

# draw a stroke (1000,500)->(1200,620) inside the selection
[Sim50]::Drag(1000, 500, 1200, 620, 12)
Start-Sleep -Milliseconds 250

# hover outside right -> SizeWE, click -> expand right edge to 1700
$curR = [Sim50]::CursorAt(1700, 650)
[Sim50]::Click(1700, 650)
$ok1 = $false
for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 50; if ((TailLines 'Selection expanded').Count -ge 1) { $ok1 = $true; break } }

# hover below -> SizeNS, click -> expand bottom edge to 980
$curB = [Sim50]::CursorAt(1250, 980)
[Sim50]::Click(1250, 980)
$ok2 = $false
for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 50; if ((TailLines 'Selection expanded').Count -ge 2) { $ok2 = $true; break } }

# hover top-left corner -> SizeNWSE, click -> expand left+top to (800,350)
$curTL = [Sim50]::CursorAt(800, 350)
[Sim50]::Click(800, 350)
$ok3 = $false
for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 50; if ((TailLines 'Selection expanded').Count -ge 3) { $ok3 = $true; break } }

# hover bottom-right corner -> SizeNWSE, click -> expand right+bottom to (1750,1010)
$curBR = [Sim50]::CursorAt(1750, 1010)
[Sim50]::Click(1750, 1010)
$ok4 = $false
for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 50; if ((TailLines 'Selection expanded').Count -ge 4) { $ok4 = $true; break } }

Write-Output ("EXPAND=" + $ok1 + $ok2 + $ok3 + $ok4 + " cursors R=" + $curR + " B=" + $curB + " TL=" + $curTL + " BR=" + $curBR)

# double-click inside -> copy; final selection (800,350)-(1750,1010) = 950x660
Start-Sleep -Milliseconds 300
[Sim50]::DblClick(1200, 700)
Start-Sleep -Milliseconds 900

$w = 0; $h = 0; $pxok = $false
try {
  $img = [System.Windows.Forms.Clipboard]::GetImage()
  if ($img -ne $null) {
    $w = $img.Width; $h = $img.Height
    $c = $img.GetPixel(300, 210)
    if ($c.R -gt 150 -and $c.G -lt 120 -and $c.B -lt 120) { $pxok = $true }
    Write-Output ("STROKEPX=" + $c.R + "," + $c.G + "," + $c.B)
  } else { Write-Output 'CLIP_NULL' }
} catch { Write-Output ("CLIP_ERR=" + $_.Exception.Message) }
Write-Output ("CLIP=" + $w + "x" + $h)

if ($ok1 -and $ok2 -and $ok3 -and $ok4 -and $w -ge 948 -and $w -le 952 -and $h -ge 658 -and $h -le 662 -and $pxok) {
  Write-Output 'EXPAND_OK'
} else { Write-Output 'EXPAND_FAIL' }
