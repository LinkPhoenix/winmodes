#Requires -Version 7.0
<#
.SYNOPSIS
    Rests the mouse on the Claude and the Codex part of the taskbar widget and saves a picture of the hover card window.
.DESCRIPTION
    Development helper. It moves the mouse, then asks the card window itself to draw into a bitmap (PrintWindow): nothing else
    on the screen is read. The widget window and the card (an untitled, mouse-transparent window of the WinModes process) are
    found through Windows; nothing is changed. Pictures go to the output folder as <prefix>-<name>.png.
#>
[CmdletBinding()]
param(
    [string]$OutputFolder = 'outputs',
    [string]$Prefix = 'hovercard',
    [int]$WaitMs = 1500,
    # Where to rest the mouse, as a share of the widget width from its left edge.
    [double[]]$Spots = @(0.80, 0.93),
    [string[]]$Names = @('claude', 'codex')
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class HoverProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    public static Rect? Widget() {
        var h = FindWindow(null, "WinModes taskbar widget");
        return h != IntPtr.Zero && GetWindowRect(h, out var r) ? r : null;
    }

    // The card: visible, untitled, with the transparent (0x20) and tool window (0x80) styles, owned by the given process.
    public static IntPtr Card(uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => {
            GetWindowThreadProcessId(h, out var owner);
            var style = GetWindowLong(h, -20);
            if (owner == pid && IsWindowVisible(h) && GetWindowTextLength(h) == 0 && (style & 0x20) != 0 && (style & 0x80) != 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static Rect? RectOf(IntPtr window) => GetWindowRect(window, out var r) ? r : null;
    public static void Print(IntPtr window, IntPtr dc) { PrintWindow(window, dc, 2); }
    public static void Aware() { SetProcessDPIAware(); }
}
'@

[HoverProbe]::Aware()
$process = Get-Process WinModes -ErrorAction Stop | Where-Object { $_.Path -like '*bin\Debug*' } | Select-Object -First 1
$widget = [HoverProbe]::Widget()
if ($null -eq $widget) { throw 'The taskbar widget window was not found: is it shown on the taskbar?' }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$width = $widget.Right - $widget.Left
$y = [int](($widget.Top + $widget.Bottom) / 2)
"widget: left=$($widget.Left) top=$($widget.Top) right=$($widget.Right) bottom=$($widget.Bottom)"

for ($i = 0; $i -lt $Spots.Count; $i++) {
    $x = [int]($widget.Left + $width * $Spots[$i])
    # Away first, so the next card starts from nothing.
    [HoverProbe]::SetCursorPos($widget.Left - 200, $y - 300) | Out-Null
    Start-Sleep -Milliseconds 600
    [HoverProbe]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds $WaitMs
    $card = [HoverProbe]::Card([uint32]$process.Id)
    if ($card -eq [IntPtr]::Zero) { "no card at x=$x"; continue }
    $path = Join-Path (Resolve-Path $OutputFolder) "$Prefix-$($Names[$i]).png"
    $box = [HoverProbe]::RectOf($card)
    "card: left=$($box.Left) top=$($box.Top) right=$($box.Right) bottom=$($box.Bottom)"
    $bitmap = New-Object System.Drawing.Bitmap ($box.Right - $box.Left), ($box.Bottom - $box.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [HoverProbe]::Print($card, $dc)
    $graphics.ReleaseHdc($dc)
    $bitmap.Save($path)
    $graphics.Dispose(); $bitmap.Dispose()
    "saved $path (mouse at x=$x)"
}
[HoverProbe]::SetCursorPos($widget.Left - 200, $y - 300) | Out-Null
Start-Sleep -Milliseconds 400
"card hidden after the mouse left: $([HoverProbe]::Card([uint32]$process.Id) -eq [IntPtr]::Zero)"
