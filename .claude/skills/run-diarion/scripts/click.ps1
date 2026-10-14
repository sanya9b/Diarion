param([int]$X, [int]$Y, [int]$HoldMs = 60)

# Screen coordinates, not image coordinates. Add the window origin that shot.ps1 printed.
# -HoldMs 1000 is a press-and-hold, for long-press gestures (TouchBehavior.LongPressCommand).

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Click {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
  public const uint DOWN = 0x0002, UP = 0x0004;
}
"@

[void][Click]::SetCursorPos($X, $Y)
Start-Sleep -Milliseconds 200
[Click]::mouse_event([Click]::DOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[Click]::mouse_event([Click]::UP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
Write-Output ("clicked {0},{1}" -f $X, $Y)
