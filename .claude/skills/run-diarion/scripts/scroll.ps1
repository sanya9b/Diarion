param([int]$X, [int]$Y, [int]$Notches = -3)

# Negative notches scroll down. Screen coordinates, as with click.ps1.

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Wheel {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  // dwData is declared signed on purpose. It is a DWORD in the Win32 header, but a scroll down is a
  // negative delta and marshalling it through uint throws before it ever reaches the window.
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
  public const uint WHEEL = 0x0800;
}
"@

[void][Wheel]::SetCursorPos($X, $Y)
Start-Sleep -Milliseconds 200
[Wheel]::mouse_event([Wheel]::WHEEL, 0, 0, ($Notches * 120), [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
Write-Output ("scrolled {0} at {1},{2}" -f $Notches, $X, $Y)
