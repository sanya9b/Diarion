param([int]$FromX, [int]$FromY, [int]$ToX, [int]$ToY, [int]$Steps = 25)

# Press, move in steps, release. The steps matter: a swipe recognizer needs a stream of intermediate
# positions to read a direction and a velocity from. Jumping straight to the end looks like a click that
# happened to land somewhere else, and nothing is recognized.

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Drag {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
  public const uint DOWN = 0x0002, UP = 0x0004;
}
"@

[void][Drag]::SetCursorPos($FromX, $FromY)
Start-Sleep -Milliseconds 250
[Drag]::mouse_event([Drag]::DOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 120

for ($i = 1; $i -le $Steps; $i++) {
  $x = [int]($FromX + ($ToX - $FromX) * $i / $Steps)
  $y = [int]($FromY + ($ToY - $FromY) * $i / $Steps)
  [void][Drag]::SetCursorPos($x, $y)
  Start-Sleep -Milliseconds 16
}

Start-Sleep -Milliseconds 150
[Drag]::mouse_event([Drag]::UP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
Write-Output ("dragged {0},{1} -> {2},{3}" -f $FromX, $FromY, $ToX, $ToY)
