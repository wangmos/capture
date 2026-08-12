# Long-shot test: put a REAL scrollable window on screen with known tall content,
# run the app-driven scroll capture, assert it stitched far beyond one viewport.
# Pure ASCII. The form lives in this process, so we must pump its message loop
# (DoEvents) while waiting, otherwise it will never repaint or scroll.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimLs {
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
  public static void Up() {
    var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Size);
  }
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(120); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(20); }
    Up(); System.Threading.Thread.Sleep(80);
  }
}
"@

# --- a real scrollable window with 300 distinct lines ---
$lines = @()
for ($i = 1; $i -le 300; $i++) { $lines += ('Line {0:0000}  content marker {1}' -f $i, ($i * 7919 % 100000)) }

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 900, 400
$form.Size = New-Object System.Drawing.Size 700, 500
$form.TopMost = $true
$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true
$box.ReadOnly = $true
$box.WordWrap = $false
$box.ScrollBars = 'Vertical'
$box.Dock = 'Fill'
$box.Font = New-Object System.Drawing.Font 'Consolas', 12
$box.BackColor = [System.Drawing.Color]::White
$box.Text = ($lines -join "`r`n")
$form.Controls.Add($box)
$form.Show()
$box.Select(0, 0)

function Pump([int]$ms) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $ms) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 20
  }
}
Pump 600

$log = Join-Path $env:TEMP 'wec_log.txt'
$base = 0
if (Test-Path $log) { $base = @(Get-Content $log).Count }

Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' | Out-Null

function TailLines([string]$pat) {
  try {
    $lines2 = @(Get-Content $log -Encoding UTF8)
    if ($lines2.Count -lt $base) { $base = 0 }   # log was rotated since we snapshotted
    if ($lines2.Count -le $base) { return @() }
    return @($lines2[$base..($lines2.Count-1)] | Where-Object { $_ -match $pat })
  } catch { return @() }
}

$ready = $false
for ($i = 0; $i -lt 60; $i++) { Pump 100; if ((TailLines 'OverlayWindow shown').Count -gt 0) { $ready = $true; break } }
if (-not $ready) { Write-Output 'OVERLAY_TIMEOUT'; $form.Close(); exit 1 }
Pump 300

# select inside the text area, excluding the vertical scrollbar on the right
[SimLs]::Drag(915, 415, 1560, 880, 15)
Pump 500

$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -eq 0) { Write-Output 'NO_TOOLBAR_RECTS'; $form.Close(); taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }
if ($rects[-1] -notmatch 'longshot=(\d+),(\d+)') { Write-Output 'NO_LONGSHOT_BUTTON'; $form.Close(); taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }
$lsX = [int]$Matches[1]; $lsY = [int]$Matches[2]
Write-Output ("LONGSHOT_BTN=" + $lsX + "," + $lsY)

[SimLs]::Click($lsX, $lsY)

# keep pumping the form's message loop so it actually scrolls while the app drives it
$done = @()
for ($i = 0; $i -lt 400; $i++) {
  Pump 150
  $done = @(TailLines 'LongShot done|LongShot FAILED')
  if ($done.Count -gt 0) { break }
}

$start = @(TailLines 'LongShot start')
if ($start.Count -gt 0) { Write-Output ('START ' + ($start[-1] -replace '^\S+\s+', '')) }

if ($done.Count -eq 0) {
  Write-Output 'LONGSHOT_TIMEOUT'
} else {
  Write-Output ('RESULT ' + ($done[-1] -replace '^\S+\s+', ''))
  $h = 0
  if ($done[-1] -match 'height=(\d+)') { $h = [int]$Matches[1] }
  $lowConf = @(TailLines 'low confidence').Count
  Write-Output ('STITCHED_HEIGHT=' + $h + ' ROLLBACKS=' + $lowConf)
  if ($done[-1] -match 'LongShot done' -and $h -gt 2000) { Write-Output 'LONGSHOT_OK' }
  else { Write-Output 'LONGSHOT_FAIL' }

  # screenshot the preview window for design review
  Pump 1200
  Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinFind {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool Proc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Proc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
  public static RECT FindBig(int minW, int minH, out bool ok) {
    RECT found = new RECT(); bool got = false;
    System.Diagnostics.Process[] ps = System.Diagnostics.Process.GetProcessesByName("WeCapture");
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
  # poll: writing a multi-MB bitmap to the clipboard before Show() can take a while
  $wok = $false
  $wr = $null
  for ($t = 0; $t -lt 40; $t++) {
    $wr = [WinFind]::FindBig(400, 400, [ref]$wok)
    if ($wok) { break }
    Pump 200
  }
  if ($wok) {
    $pad = 8
    $px = [Math]::Max(0, $wr.Left - $pad); $py = [Math]::Max(0, $wr.Top - $pad)
    $pw = ($wr.Right - $wr.Left) + $pad * 2; $ph = ($wr.Bottom - $wr.Top) + $pad * 2
    $b2 = New-Object System.Drawing.Bitmap $pw, $ph
    $g2 = [System.Drawing.Graphics]::FromImage($b2)
    $g2.CopyFromScreen($px, $py, 0, 0, (New-Object System.Drawing.Size $pw, $ph))
    $b2.Save('C:\temp\ui_longshot.png', [System.Drawing.Imaging.ImageFormat]::Png)
    $g2.Dispose(); $b2.Dispose()
    Write-Output ('PREVIEW_SHOT ' + $pw + 'x' + $ph)
  } else {
    Write-Output 'PREVIEW_WINDOW_NOT_FOUND'
    $sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $fb = New-Object System.Drawing.Bitmap $sb.Width, $sb.Height
    $fg = [System.Drawing.Graphics]::FromImage($fb)
    $fg.CopyFromScreen($sb.Location, [System.Drawing.Point]::Empty, $sb.Size)
    $fb.Save('C:\temp\ui_longshot_fullscreen.png', [System.Drawing.Imaging.ImageFormat]::Png)
    $fg.Dispose(); $fb.Dispose()
    Write-Output 'FULLSCREEN_SAVED'
  }

  # save the stitched image so its CONTENT can be verified (height alone proves nothing)
  try {
    $img = [System.Windows.Forms.Clipboard]::GetImage()
    if ($img -ne $null) {
      $img.Save('C:\temp\longshot_result.png', [System.Drawing.Imaging.ImageFormat]::Png)
      Write-Output ('SAVED C:\temp\longshot_result.png ' + $img.Width + 'x' + $img.Height)
    } else { Write-Output 'NO_CLIPBOARD_IMAGE' }
  } catch { Write-Output ('CLIPBOARD_ERR ' + $_.Exception.Message) }
}

$form.Close()
Start-Sleep -Milliseconds 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
