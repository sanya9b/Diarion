param([int]$X, [int]$Y, [int]$HoldMs = 60, [int]$ExpectPid = 0)

# Screen coordinates, not image coordinates. Add the window origin that shot.ps1 printed.
# -HoldMs 1000 is a press-and-hold, for long-press gestures (TouchBehavior.LongPressCommand).

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Click {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public const uint DOWN = 0x0002, UP = 0x0004;
}
"@

# -ExpectPid refuses to click unless that process owns the window under the point. With the owner's
# real diary open next to a sandbox instance, a click into the wrong window writes into the real
# diary. The window under the point, not the foreground one: a hidden dialog host (PickerHost) can
# hold the keyboard focus while the target is plainly the topmost thing painted at (X, Y).
if ($ExpectPid -gt 0) {
  $pt = New-Object Click+POINT; $pt.X = $X; $pt.Y = $Y
  $root = [Click]::GetAncestor([Click]::WindowFromPoint($pt), 2)  # GA_ROOT
  $underPid = [uint32]0
  [void][Click]::GetWindowThreadProcessId($root, [ref]$underPid)
  if ([int]$underPid -ne $ExpectPid) { Write-Output ("WINDOW_MISMATCH pid={0}" -f $underPid); exit 1 }
}

[void][Click]::SetCursorPos($X, $Y)
Start-Sleep -Milliseconds 200
[Click]::mouse_event([Click]::DOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[Click]::mouse_event([Click]::UP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
Write-Output ("clicked {0},{1}" -f $X, $Y)
