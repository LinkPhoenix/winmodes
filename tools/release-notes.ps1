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

# A beta has no section of its own: its notes are what the stable release will list.
$beta = $version.Contains('-beta.')
if (-not $notes -and $beta -and (Test-Path -LiteralPath $changelog)) {
    $unreleased = [regex]::Match((Get-Content -LiteralPath $changelog -Raw), '(?ms)^## \[Unreleased\][^\n]*\n(.*?)(?=^## \[|\z)')
    if ($unreleased.Success) { $notes = $unreleased.Groups[1].Value.Trim() }
}

if (-not $notes) {
    $previous = git -C $Root describe --tags --abbrev=0 "$Tag^" 2>$null
    $range = $previous ? "$previous..$Tag" : $Tag
    $commits = git -C $Root log $range --no-merges --pretty=format:'- %s'
    $notes = "## Changes`n`n" + ($commits -join "`n")
}

$download = "Download ``WinModes-$Tag-setup-win-x64.exe`` to install, or ``WinModes-$Tag-portable-win-x64.zip``, extract it and run ``WinModes.exe``. No .NET runtime is needed."
$banner = $beta ? "> **Beta.** This is a pre-release for testing, not the stable version. A stable copy of WinModes never offers it as an update, and a beta copy moves to the stable version when it comes out. Please report what you find in the issues.`n`n" : ''
Set-Content -LiteralPath $OutputPath -Value "$banner$notes`n`n---`n`n$download`n"
"Wrote $OutputPath"
