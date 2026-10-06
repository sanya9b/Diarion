param([int]$Width = 390, [int]$Height = 844, [int]$Left = 60, [int]$Top = 60)

# Diarion ships to Android and iOS. Judging layout at desktop width answers a question nobody asked, so
# resize to a phone before looking. A proxy, not a device: DPI and text scaling still differ.

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Move {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

$deadline = (Get-Date).AddSeconds(60)
$proc = $null
while ((Get-Date) -lt $deadline) {
  $proc = Get-Process -Name Diarion -ErrorAction SilentlyContinue |
          Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if ($proc) { break }
  Start-Sleep -Milliseconds 400
}
if (-not $proc) { Write-Output "NO_WINDOW"; exit 1 }

[void][Move]::SetForegroundWindow($proc.MainWindowHandle)
[void][Move]::MoveWindow($proc.MainWindowHandle, $Left, $Top, $Width, $Height, $true)
Start-Sleep -Milliseconds 900
Write-Output ("resized to {0}x{1} at {2},{3}" -f $Width, $Height, $Left, $Top)
