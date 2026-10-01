#Requires -Version 7.0
<#
.SYNOPSIS
    Captures the WinModes main window to a PNG, optionally clicking a button first (UI Automation).
.DESCRIPTION
    Development helper for visual checks. It captures only the WinModes window, never the desktop.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$InvokeButtonNamed,
    [int]$ButtonIndex = 0,
    [int]$WaitAfterInvokeSeconds = 3
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace WinModesTools -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr dc, uint flags);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
public struct RECT { public int L, T, R, B; }
'@

$PrintWindowRenderFullContent = 2

[WinModesTools.Native]::SetProcessDPIAware() | Out-Null
$process = Get-Process -Name WinModes | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (-not $process) { throw 'WinModes is not running or has no visible window.' }

if ($InvokeButtonNamed) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    # Navigation items are not buttons, so match by name and keep only invokable elements.
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $InvokeButtonNamed)
    $buttons = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
        Where-Object { $_.GetSupportedPatterns() -contains [System.Windows.Automation.InvokePattern]::Pattern })
    if ($buttons.Count -le $ButtonIndex) { throw "Invokable element '$InvokeButtonNamed' #$ButtonIndex not found ($($buttons.Count) match)." }
    $buttons[$ButtonIndex].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds $WaitAfterInvokeSeconds
}

$rect = New-Object WinModesTools.Native+RECT
[WinModesTools.Native]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
$bitmap = [System.Drawing.Bitmap]::new($rect.R - $rect.L, $rect.B - $rect.T)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $dc = $graphics.GetHdc()
    [WinModesTools.Native]::PrintWindow($process.MainWindowHandle, $dc, $PrintWindowRenderFullContent) | Out-Null
    $graphics.ReleaseHdc($dc)
    $bitmap.Save($OutputPath)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
"Saved $OutputPath"
