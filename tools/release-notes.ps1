#Requires -Version 7.0
<#
.SYNOPSIS
    Writes the release notes for a tag: the matching section of CHANGELOG.md, or the commits since the previous tag.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$version = $Tag.TrimStart('v')
$notes = $null

$changelog = Join-Path $Root 'CHANGELOG.md'
if (Test-Path -LiteralPath $changelog) {
    # A section starts at "## [1.2.3]" and ends at the next "## [" heading.
    $pattern = "(?ms)^## \[$([regex]::Escape($version))\][^\n]*\n(.*?)(?=^## \[|\z)"
    $match = [regex]::Match((Get-Content -LiteralPath $changelog -Raw), $pattern)
    if ($match.Success) { $notes = $match.Groups[1].Value.Trim() }
}

if (-not $notes) {
    $previous = git -C $Root describe --tags --abbrev=0 "$Tag^" 2>$null
    $range = $previous ? "$previous..$Tag" : $Tag
    $commits = git -C $Root log $range --no-merges --pretty=format:'- %s'
    $notes = "## Changes`n`n" + ($commits -join "`n")
}

$download = "Download ``WinModes-$Tag-win-x64.zip``, extract it and run ``WinModes.exe``. No installation or .NET runtime is needed."
Set-Content -LiteralPath $OutputPath -Value "$notes`n`n---`n`n$download`n"
"Wrote $OutputPath"
