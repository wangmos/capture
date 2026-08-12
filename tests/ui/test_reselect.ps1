# reselect regression: press OUTSIDE + drag = new selection clearing annotations (not an expand)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Sim51 {
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
try { [System.Windows.Forms.Clipboard]::Clear() } catch { }
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
[Sim51]::Drag(900, 400, 1600, 900, 15)
Start-Sleep -Milliseconds 400

# Locate the pen button from the app's own ToolbarRects log line.
# Never hardcode/probe: the toolbar is right-anchored, so every added button shifts
# the tools left, and a wrong click opens the style panel which moves the whole row down.
$pen = $false
$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -gt 0 -and $rects[-1] -match 'Pen=(\d+),(\d+)') {
  $px = [int]$Matches[1]; $py = [int]$Matches[2]
  [Sim51]::Click($px, $py)
  Start-Sleep -Milliseconds 500
  if ((TailLines 'active=Pen').Count -gt 0) { Write-Output ("PENBTN=" + $px + "," + $py); $pen = $true }
} else { Write-Output 'NO_TOOLBAR_RECTS' }
Write-Output ("pen=" + $pen)
if (-not $pen) { Write-Output 'RESELECT_FAIL pen'; exit 1 }

# stroke (1000,500)->(1200,620)
[Sim51]::Drag(1000, 500, 1200, 620, 12)
Start-Sleep -Milliseconds 250

# press OUTSIDE top-left (800,350) + drag over old stroke area -> NEW selection, annotations cleared
[Sim51]::Drag(800, 350, 1300, 700, 15)
Start-Sleep -Milliseconds 400

# no expand log expected; new selection (800,350)-(1300,700) = 500x350
$expCount = (TailLines 'Selection expanded').Count
Write-Output ("expandedLines=" + $expCount)

$rects2 = @(TailLines 'ToolbarRects')
if ($rects2.Count -gt 0 -and $rects2[-1] -match 'copy=(\d+),(\d+)') {
  [Sim51]::Click([int]$Matches[1], [int]$Matches[2])
} else { Write-Output 'NO_COPY_BUTTON' }
Start-Sleep -Milliseconds 900

$w = 0; $h = 0; $redGone = $false
try {
  $img = [System.Windows.Forms.Clipboard]::GetImage()
  if ($img -ne $null) {
    $w = $img.Width; $h = $img.Height
    # old stroke midpoint global (1100,560) -> clipboard (300,210); must NOT be the red pen color now
    $c = $img.GetPixel(300, 210)
    # match the actual pen colour (255,59,48), not "anything reddish": desktop content
    # behind the selection can easily be dark red and produce a false positive
    if (-not ($c.R -gt 230 -and $c.G -gt 25 -and $c.G -lt 95 -and $c.B -lt 85)) { $redGone = $true }
    Write-Output ("OLPX=" + $c.R + "," + $c.G + "," + $c.B)
  } else { Write-Output 'CLIP_NULL' }
} catch { Write-Output ("CLIP_ERR=" + $_.Exception.Message) }
Write-Output ("CLIP=" + $w + "x" + $h)

if ($expCount -eq 0 -and $w -ge 498 -and $w -le 502 -and $h -ge 348 -and $h -le 352 -and $redGone) {
  Write-Output 'RESELECT_OK'
} else { Write-Output 'RESELECT_FAIL' }
