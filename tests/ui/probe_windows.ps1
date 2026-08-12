# Probe the viewer + docked text window while they are open:
# does the viewer have a window icon, and is the text window kept out of the taskbar?
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Pw {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int p);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
  public delegate bool Proc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Proc p, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
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
  public static void Dump(int pid) {
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      int wp; GetWindowThreadProcessId(h, out wp);
      if (wp != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(200); GetWindowText(h, sb, 200);
      string title = sb.ToString();
      if (title.Length == 0) return true;
      int ex = GetWindowLong(h, -20);
      bool toolWin = (ex & 0x80) != 0;          // WS_EX_TOOLWINDOW
      bool appWin  = (ex & 0x40000) != 0;       // WS_EX_APPWINDOW forces it onto the taskbar
      IntPtr owner = GetWindow(h, 4);           // GW_OWNER
      // A window reaches the taskbar only if it is unowned, not a tool window - or forces it.
      bool inTaskbar = appWin || (owner == IntPtr.Zero && !toolWin);
      IntPtr icon = SendMessage(h, 0x007F, new IntPtr(1), IntPtr.Zero);   // WM_GETICON / ICON_BIG
      if (icon == IntPtr.Zero) icon = SendMessage(h, 0x007F, IntPtr.Zero, IntPtr.Zero);
      Console.WriteLine("  '" + title + "'  inTaskbar=" + inTaskbar + "  owned=" + (owner != IntPtr.Zero) + "  toolWindow=" + toolWin + "  hasIcon=" + (icon != IntPtr.Zero));
      return true;
    }, IntPtr.Zero);
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
$label.Text = "Window icon and taskbar probe`r`nSecond line 12345"
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
$proc = Start-Process 'D:\works\c++\capture\bin\Debug\net10.0-windows\WeCapture.exe' -ArgumentList '--capture' -PassThru

function TailLines([string]$pat) {
  try {
    $l = @(Get-Content $log -Encoding UTF8)
    if ($l.Count -lt $base) { $base = 0 }
    if ($l.Count -le $base) { return @() }
    return @($l[$base..($l.Count-1)] | Where-Object { $_ -match $pat })
  } catch { return @() }
}

for ($i = 0; $i -lt 60; $i++) { Pump 100; if ((TailLines 'OverlayWindow shown').Count -gt 0) { break } }
Pump 300
[Pw]::Drag(905, 405, 1595, 690, 15)
Pump 500

$rects = @(TailLines 'ToolbarRects')
if ($rects.Count -eq 0 -or $rects[-1] -notmatch 'ocr=(\d+),(\d+)') { Write-Output 'NO_OCR_BUTTON'; $form.Close(); exit 1 }
[Pw]::Click([int]$Matches[1], [int]$Matches[2])

for ($i = 0; $i -lt 80; $i++) { Pump 150; if ((TailLines 'ViewerRects').Count -gt 0) { break } }
Pump 1500
$form.Hide(); Pump 300

Write-Output 'WeCapture visible windows:'
[Pw]::Dump($proc.Id)

$form.Close()
Pump 200
taskkill /F /IM WeCapture.exe 2>$null | Out-Null
