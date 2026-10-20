param([string]$Out = "shot.png", [switch]$NoRaise, [int]$ProcessId = 0)

# Captures the Diarion window to a PNG. Waits for the window rather than sleeping blindly, because a
# cold MAUI start is slow and how slow varies.

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$deadline = (Get-Date).AddSeconds(60)
$proc = $null
while ((Get-Date) -lt $deadline) {
  # -ProcessId pins one instance when two are running (the owner's real diary next to a sandbox);
  # without it the first Diarion window wins, which may be the wrong one.
  $candidates = if ($ProcessId -gt 0) { Get-Process -Id $ProcessId -ErrorAction SilentlyContinue }
                else { Get-Process -Name Diarion -ErrorAction SilentlyContinue }
  $proc = $candidates | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if ($proc) { break }
  Start-Sleep -Milliseconds 400
}
if (-not $proc) { Write-Output "NO_WINDOW"; exit 1 }

if (-not $NoRaise) {
  # Windows lets a background process take the foreground only right after a key event. A tap of
  # Alt (down, up) is the usual key; without it the raise silently fails while the user is active.
  [Win]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
  [Win]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [void][Win]::SetForegroundWindow($proc.MainWindowHandle)
  Start-Sleep -Milliseconds 400
}

$r = New-Object Win+RECT
[void][Win]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
if ($w -le 0 -or $h -le 0) { Write-Output "BAD_RECT"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)

if ($NoRaise) {
  # Asks the window to draw itself, so an application sitting on top of it does not end up in the
  # picture. Use when the user is working in another window and stealing the foreground would be rude.
  # PW_RENDERFULLCONTENT = 2; without it a composition-rendered window comes back blank.
  $hdc = $g.GetHdc()
  [void][Win]::PrintWindow($proc.MainWindowHandle, $hdc, 2)
  $g.ReleaseHdc($hdc)
} else {
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
}

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# The origin is the whole point of this line: image pixel (x,y) is screen (left+x, top+y), which is how
# a coordinate read off the screenshot becomes one you can click.
Write-Output ("OK left={0} top={1} w={2} h={3} pid={4}" -f $r.Left, $r.Top, $w, $h, $proc.Id)
