#Requires -Version 7.0
<#
.SYNOPSIS
    Presses the Windows key twice (opens then closes the Start menu) and captures the taskbar at short intervals.
.DESCRIPTION
    Development helper to look for taskbar flicker while the WinModes taskbar widget is on. It sends key presses and reads
    the screen; it changes nothing. Pictures go to the output folder as <prefix>-<milliseconds>.png.
#>
[CmdletBinding()]
param(
    [string]$OutputFolder = 'outputs',
    [string]$Prefix = 'startmenu',
    [int[]]$DelaysAfterCloseMs = @(60, 150, 300, 500, 800, 1200, 2000)
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Keys2 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void TapWindowsKey() { keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
}
'@

New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$strip = 70

function Save-Taskbar([string]$name) {
    # The whole width, scaled to half, so a missing icon stands out.
    $full = New-Object System.Drawing.Bitmap $screen.Width, $strip
    $g = [System.Drawing.Graphics]::FromImage($full)
    $g.CopyFromScreen(0, $screen.Bottom - $strip, 0, 0, $full.Size)
    $half = New-Object System.Drawing.Bitmap ([int]($screen.Width / 2)), ([int]($strip / 2))
    $g2 = [System.Drawing.Graphics]::FromImage($half)
    $g2.InterpolationMode = 'HighQualityBicubic'
    $g2.DrawImage($full, 0, 0, $half.Width, $half.Height)
    $half.Save((Join-Path $OutputFolder "$Prefix-$name.png"))
    $full.Dispose(); $half.Dispose()
}

Save-Taskbar 'before'
[Keys2]::TapWindowsKey()
Start-Sleep -Milliseconds 900
Save-Taskbar 'open'
[Keys2]::TapWindowsKey()
$watch = [Diagnostics.Stopwatch]::StartNew()
foreach ($delay in $DelaysAfterCloseMs) {
    $wait = $delay - $watch.ElapsedMilliseconds
    if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    Save-Taskbar ("after{0:0000}" -f $delay)
}
