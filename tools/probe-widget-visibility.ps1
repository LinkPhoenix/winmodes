#Requires -Version 7.0
<#
.SYNOPSIS
    Opens and closes the Start menu with the Windows key several times and samples whether the WinModes taskbar widget stays visible.
.DESCRIPTION
    Development helper. It sends key presses and reads the visibility flag of the widget window (found by its title); it
    changes nothing. Prints every sample where the window is missing or hidden, then a summary.
#>
[CmdletBinding()]
param(
    [int]$Cycles = 8,
    [int]$SampleEveryMs = 30
)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WidgetProbe {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static string State() {
        var h = FindWindow(null, "WinModes taskbar widget");
        return h == IntPtr.Zero ? "absent" : IsWindowVisible(h) ? "visible" : "HIDDEN";
    }
    public static void TapWindowsKey() { keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
}
'@

$bad = 0
$samples = 0
for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
    # Alternate slow and quick presses: the quick ones catch the menu while it is still animating.
    $openFor = if ($cycle % 2 -eq 1) { 900 } else { 250 }
    foreach ($phase in @(@{ Name = 'open'; Ms = $openFor }, @{ Name = 'closed'; Ms = 1800 })) {
        [WidgetProbe]::TapWindowsKey()
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.ElapsedMilliseconds -lt $phase.Ms) {
            $state = [WidgetProbe]::State()
            $samples++
            if ($state -ne 'visible') { $bad++; "cycle $cycle $($phase.Name) +$($clock.ElapsedMilliseconds) ms: $state" }
            Start-Sleep -Milliseconds $SampleEveryMs
        }
    }
}
"samples=$samples notVisible=$bad"
