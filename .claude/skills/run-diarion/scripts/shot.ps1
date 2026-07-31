param([string]$Out = "shot.png")

# Captures the Diarion window to a PNG. Waits for the window rather than sleeping blindly, because a
# cold MAUI start is slow and how slow varies.

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
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

[void][Win]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Milliseconds 400

$r = New-Object Win+RECT
[void][Win]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
if ($w -le 0 -or $h -le 0) { Write-Output "BAD_RECT"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# The origin is the whole point of this line: image pixel (x,y) is screen (left+x, top+y), which is how
# a coordinate read off the screenshot becomes one you can click.
Write-Output ("OK left={0} top={1} w={2} h={3}" -f $r.Left, $r.Top, $w, $h)
