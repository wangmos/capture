# Verify custom shortcut bindings persist and take effect (backs up/restores real settings).
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SimC {
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
  public static void Tap(ushort vk) {
    var d = new INPUT(); d.type = 1; d.U.ki.wVk = vk; SendInput(1, new[]{d}, Size);
    System.Threading.Thread.Sleep(35);
    var u = new INPUT(); u.type = 1; u.U.ki.wVk = vk; u.U.ki.dwFlags = KEYUP; SendInput(1, new[]{u}, Size);
    System.Threading.Thread.Sleep(200);
  }
}
"@

$dir = Join-Path $env:APPDATA 'WeCapture'
$file = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP 'wec_settings_backup.json'
$had = Test-Path $file
if ($had) { Copy-Item $file $backup -Force }

try {
  New-Item -ItemType Directory -Force $dir | Out-Null
  # Rebind the rectangle tool from R to G, leaving everything else default.
  # Do NOT hand-write Hotkey: System.Text.Json rejects string enum values by default,
  # which makes the whole file fail to parse and silently reverts to defaults.
  $json = @'
{
  "AutoTextSelect": true,
  "Shortcuts": { "ToolRectangle": "G" }
}
'@
  Set-Content -Path $file -Value $json -Encoding UTF8

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

  [SimC]::Drag(900, 400, 1600, 900, 15)
  Start-Sleep -Milliseconds 500

  # G is now bound to the rectangle tool
  [SimC]::Tap(0x47)
  Start-Sleep -Milliseconds 250
  $g = @(TailLines 'SetTool Rectangle active=Rectangle').Count
  Write-Output ('CUSTOM_G_BINDS_RECTANGLE=' + ($g -gt 0))

  # R must no longer do anything (its binding moved to G)
  $beforeR = @(TailLines 'SetTool').Count
  [SimC]::Tap(0x52)
  Start-Sleep -Milliseconds 250
  $afterR = @(TailLines 'SetTool').Count
  Write-Output ('OLD_R_INACTIVE=' + ($afterR -eq $beforeR))

  if ($g -gt 0 -and $afterR -eq $beforeR) { Write-Output 'CUSTOM_SHORTCUT_OK' } else { Write-Output 'CUSTOM_SHORTCUT_FAIL' }
}
finally {
  taskkill /F /IM WeCapture.exe 2>$null | Out-Null
  Start-Sleep -Milliseconds 300
  if ($had) { Copy-Item $backup $file -Force } else { Remove-Item $file -ErrorAction SilentlyContinue }
  Write-Output 'SETTINGS_RESTORED'
}
