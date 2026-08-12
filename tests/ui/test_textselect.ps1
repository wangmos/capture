# On-image text selection test: OCR text layer -> drag select -> Ctrl+C -> clipboard.
# Button coords are READ FROM THE LOG (ToolbarRects), not hardcoded. Pure ASCII.
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimTs {
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
  public static void Up() {
    var u = new INPUT(); u.type = 0; u.U.mi.dwFlags = LEFTUP; SendInput(1, new[]{u}, Size);
  }
  public static void Click(int x, int y) { Down(x, y); System.Threading.Thread.Sleep(15); Up(); System.Threading.Thread.Sleep(120); }
  public static void Drag(int x1, int y1, int x2, int y2, int steps) {
    Down(x1, y1);
    for (int t = 1; t <= steps; t++) { SetCursorPos(x1 + (x2-x1)*t/steps, y1 + (y2-y1)*t/steps); System.Threading.Thread.Sleep(20); }
    Up(); System.Threading.Thread.Sleep(80);
  }
  public static void Key(ushort vk, bool up) {
    var i = new INPUT(); i.type = 1; i.U.ki.wVk = vk; i.U.ki.dwFlags = up ? KEYUP : 0;
    SendInput(1, new[]{i}, Size);
  }
  public static void CtrlC() {
    Key(0x11, false); System.Threading.Thread.Sleep(30);
    Key(0x43, false); System.Threading.Thread.Sleep(40);
    Key(0x43, true);  System.Threading.Thread.Sleep(30);
    Key(0x11, true);  System.Threading.Thread.Sleep(120);
  }
}
"@

# Put a window with KNOWN text under the capture region, so the assertion does not
# depend on whatever happens to be on screen.
$expected = @('WeCapture OCR Test Line One', 'Second Line 12345', 'Third Line ABCDEFG')
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 900, 400
$form.Size = New-Object System.Drawing.Size 700, 500
$form.BackColor = [System.Drawing.Color]::White
$form.TopMost = $true
$label = New-Object System.Windows.Forms.Label
$label.AutoSize = $false
$label.Dock = 'Fill'
$label.Font = New-Object System.Drawing.Font 'Segoe UI', 20
$label.ForeColor = [System.Drawing.Color]::Black
$label.Padding = New-Object System.Windows.Forms.Padding 24, 24, 0, 0
$label.Text = ($expected -join "`r`n")
$form.Controls.Add($label)
$form.Show()
for ($i = 0; $i -lt 12; $i++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 }

$log = Join-Path $env:TEMP 'wec_log.txt'
$base = 0
if (Test-Path $log) { $base = @(Get-Content $log).Count }

Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' | Out-Null
try { [System.Windows.Forms.Clipboard]::Clear() } catch { }

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

# 1) make a selection over on-screen text
[SimTs]::Drag(900, 400, 1600, 900, 15)
Start-Sleep -Milliseconds 400

# 2) text-select must be entered AUTOMATICALLY after the selection is confirmed
$auto = @(TailLines 'AutoTextSelect entered')
if ($auto.Count -eq 0) { Write-Output 'AUTO_TEXTSELECT_MISSING'; taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }
Write-Output 'AUTO_TEXTSELECT_OK'

# 3) the manual toggle must still work: click it twice (off, then on again)
$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -eq 0) { Write-Output 'NO_TOOLBAR_RECTS'; exit 1 }
$last = $rects[-1]
if ($last -notmatch 'TextSelect=(\d+),(\d+)') { Write-Output 'NO_TEXTSELECT_BUTTON'; exit 1 }
$tsX = [int]$Matches[1]; $tsY = [int]$Matches[2]
Write-Output ("TEXTSELECT_BTN=" + $tsX + "," + $tsY)
[SimTs]::Click($tsX, $tsY)          # expect: turns text-select OFF
Start-Sleep -Milliseconds 300
$toggles = @(TailLines 'SetTool TextSelect')
Write-Output ('MANUAL_TOGGLES=' + $toggles.Count)

# drive it back ON from the logged state (a stray click must not leave the mode off)
$on = $false
for ($i = 0; $i -lt 4; $i++) {
  $st = @(TailLines 'SetTool TextSelect')
  if ($st.Count -gt 0 -and $st[-1] -match 'active=TextSelect') { $on = $true; break }
  [SimTs]::Click($tsX, $tsY)
  Start-Sleep -Milliseconds 300
}
if (-not $on) { Write-Output 'TEXTSELECT_MODE_NOT_ON'; $form.Close(); taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }
Write-Output 'MODE_ON_CONFIRMED'

$layer = @()
for ($i = 0; $i -lt 300; $i++) {
  Start-Sleep -Milliseconds 100
  $layer = @(TailLines 'TextLayer ready')
  if ($layer.Count -gt 0) { break }
}
if ($layer.Count -eq 0) { Write-Output 'TEXTLAYER_TIMEOUT'; taskkill /F /IM WeCapture.exe 2>$null | Out-Null; exit 1 }

$chars = 0
if ($layer[-1] -match 'chars=(\d+)') { $chars = [int]$Matches[1] }
Write-Output ('TEXTLAYER ' + ($layer[-1] -replace '^.*TextLayer', 'TextLayer'))

# 4) drag from ON the first text line down past the last one.
# NOTE: do not start near a selection corner - the 6px resize handles win over text there.
[SimTs]::Drag(930, 432, 1400, 560, 25)
Start-Sleep -Milliseconds 250

# 5) Ctrl+C copies the selected text and ends the session
[SimTs]::CtrlC()

$copied = @()
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 100
  $copied = @(TailLines 'Text selection copied')
  if ($copied.Count -gt 0) { break }
}

$clip = ''
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 100
  try { $clip = [System.Windows.Forms.Clipboard]::GetText() } catch { }
  if ($clip.Length -gt 0) { break }
}

if ($copied.Count -gt 0) { Write-Output ('COPIED_LOG ' + ($copied[-1] -replace '^.*Text selection', 'Text selection')) }
Write-Output ('CLIPBOARD_CHARS=' + $clip.Length + ' LAYER_CHARS=' + $chars)
$lineCount = @($clip -split "`n").Count
Write-Output ('CLIPBOARD_LINES=' + $lineCount)

# assert the copied text actually matches what we put on screen
$flat = ($clip -replace '\s', '')
$hits = 0
foreach ($e in $expected) {
  $needle = ($e -replace '\s', '')
  if ($flat -like ('*' + $needle + '*')) { $hits++ } else { Write-Output ('MISS: ' + $e) }
}
Write-Output ('MATCHED_LINES=' + $hits + '/' + $expected.Count)
if ($hits -eq $expected.Count) { Write-Output 'TEXTSELECT_OK' } else { Write-Output 'TEXTSELECT_FAIL' }

$form.Close()
Start-Sleep -Milliseconds 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
