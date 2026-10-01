#Requires -Version 7.0
<#
.SYNOPSIS
    Exports this PC's services, startup entries and scheduled tasks to data/raw/ (read-only on the system).
.DESCRIPTION
    The files describe one machine and may contain the user name and account identifiers,
    so data/raw/ is ignored by git. tools/build-profiles.ps1 reads the newest services export.
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$rawDirectory = Join-Path $Root 'data/raw'
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
$stamp = Get-Date -Format 'yyyy-MM-dd'

Get-CimInstance Win32_Service |
    Select-Object Name, DisplayName, StartMode, State, @{ n = 'Path'; e = { $_.PathName } }, Description |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $rawDirectory "services-$stamp.json")

Get-CimInstance Win32_StartupCommand | Select-Object Name, Location, Command |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rawDirectory "startup-$stamp.json")

Get-ScheduledTask | Where-Object { $_.State -ne 'Disabled' -and $_.TaskPath -notlike '\Microsoft\*' } |
    Select-Object TaskName, TaskPath, @{ n = 'State'; e = { "$($_.State)" } } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rawDirectory "tasks-$stamp.json")

"Exported to $rawDirectory"
