#Requires -Version 7.0
<#
.SYNOPSIS
    Generates profiles/baseline.json and one profile per mode from the knowledge base.
.DESCRIPTION
    Read-only on the system: it only reads data/ and research/ and writes profiles/.
    Selection rules are documented in profiles/README.md.
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$Modes = @('code', 'work', 'game')
$ExcludedRisks = @('never-touch', 'high')
$ExcludedTweakRisks = @('never', 'high')
# Only tweaks that take effect without reboot or reinstall belong in a switchable mode.
$ModeTweakTypes = @('registry', 'service', 'powercfg', 'task')
$StopScoreMax = 1
$EnsureRunningScore = 3
# Tweaks applied by at least this many of the 12 code-verified optimizers form the shared baseline.
$BaselineMinConsensus = 7

function Read-Json([string]$RelativePath) {
    Get-Content -LiteralPath (Join-Path $Root $RelativePath) -Raw | ConvertFrom-Json
}

function Test-WebVerified($Entry) {
    # A date alone means a fetched source confirmed the entry; "-local" only confirms identity.
    $Entry.verified -match '^\d{4}-\d{2}-\d{2}$'
}

function Get-ProtectedNames($Protected) {
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($capability in $Protected.requiredCapabilities.PSObject.Properties.Value) {
        foreach ($list in 'services', 'appx', 'features') {
            foreach ($name in @($capability.$list)) { if ($name) { [void]$names.Add($name) } }
        }
    }
    $names
}

$protected = Read-Json 'data/protected.json'
$protectedNames = Get-ProtectedNames $protected
$knowledge = foreach ($file in 'windows-services', 'third-party', 'oem-drivers') {
    Read-Json "data/db/$file.json"
}
$tweaks = Read-Json 'research/oss-optimizers/tweaks-consensus.json'

# Per-user services carry a random suffix (e.g. WpnUserService_1a2b3c); the knowledge base uses the base name.
$inventory = Get-ChildItem -LiteralPath (Join-Path $Root 'data/raw') -Filter 'services-*.json' -ErrorAction SilentlyContinue |
    Sort-Object Name | Select-Object -Last 1
if (-not $inventory) {
    throw 'No machine inventory found. Run tools/export-inventory.ps1 first.'
}

$runningState = @{}
foreach ($service in (Get-Content -LiteralPath $inventory.FullName -Raw | ConvertFrom-Json)) {
    $runningState[($service.Name -replace '_[0-9a-f]{5,}$', '')] = $service.State
}

$manual = Read-Json 'profiles/modes.manual.json'
$keepServices = @($manual.keepServices.ids)
$protectedStartupPatterns = @($protected.requiredCapabilities.'user-apps'.startupIds)

function Test-ProtectedStartup($Entry) {
    foreach ($pattern in $protectedStartupPatterns) { if ($Entry.id -like $pattern) { return $true } }
    $false
}

function Test-Eligible($Entry) {
    $Entry.kind -eq 'service' -and
    $Entry.id -notin $keepServices -and
    (Test-WebVerified $Entry) -and
    $Entry.risk -notin $ExcludedRisks -and
    -not $protectedNames.Contains($Entry.id) -and
    $Entry.currentStartMode -ne 'Disabled'
}

function Test-StopHasEffect($Entry) {
    # Stopping an already stopped Manual service changes nothing.
    $Entry.currentStartMode -eq 'Auto' -or $runningState[$Entry.id] -eq 'Running'
}

function Test-TweakAllowed($Tweak) {
    $Tweak.type -in $ModeTweakTypes -and
    $Tweak.risk -notin $ExcludedTweakRisks -and
    $Tweak.reversible -eq $true -and
    $Tweak.evidence -eq 'code' -and
    -not ($Tweak.type -eq 'service' -and $protectedNames.Contains($Tweak.target))
}

$allowedTweaks = @($tweaks | Where-Object { Test-TweakAllowed $_ })
$requiredBaseline = @($manual.baseline.required)
$baselineTweaks = @($allowedTweaks | Where-Object {
        ($_.risk -eq 'low' -and $_.consensusCount -ge $BaselineMinConsensus) -or $_.id -in $requiredBaseline
    })
$baselineIds = @($baselineTweaks.id)

$profilesDir = Join-Path $Root 'profiles'
New-Item -ItemType Directory -Force -Path $profilesDir | Out-Null

$baseline = [ordered]@{
    schemaVersion = 1
    generatedBy   = 'tools/build-profiles.ps1'
    description   = "Opt-in, applied once and shared by every mode: reversible low-risk tweaks applied by at least $BaselineMinConsensus of the 12 code-verified optimizers."
    tweaks        = @($baselineTweaks | ForEach-Object {
            [ordered]@{ id = $_.id; type = $_.type; target = $_.target; value = $_.value; consensusCount = $_.consensusCount; required = $_.id -in $requiredBaseline }
        })
}
$baseline | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $profilesDir 'baseline.json')

foreach ($mode in $Modes) {
    $eligible = @($knowledge | Where-Object { Test-Eligible $_ })
    $stop = @($eligible | Where-Object { $_.modes.$mode -le $StopScoreMax -and (Test-StopHasEffect $_) } | Sort-Object id | ForEach-Object {
            [ordered]@{ id = $_.id; setStartMode = 'Manual'; stop = $true; score = $_.modes.$mode; ramImpact = $_.ramImpact; why = $_.why }
        })
    $ensure = @($eligible | Where-Object { $_.modes.$mode -ge $EnsureRunningScore -and $_.currentStartMode -eq 'Manual' } | Sort-Object id | ForEach-Object {
            [ordered]@{ id = $_.id; start = $true; why = $_.why }
        })
    $modeTweaks = @($allowedTweaks | Where-Object { $_.id -notin $baselineIds -and $_.relevance.$mode -ge 3 } | ForEach-Object {
            [ordered]@{ id = $_.id; type = $_.type; target = $_.target; value = $_.value; risk = $_.risk }
        })
    $startupCandidates = @($knowledge | Where-Object {
            $_.kind -eq 'startup-app' -and $_.modes.$mode -eq 0 -and $_.risk -notin $ExcludedRisks -and -not (Test-ProtectedStartup $_)
        } | Sort-Object id | ForEach-Object { [ordered]@{ id = $_.id; why = $_.why } })

    $modeProfile = [ordered]@{
        schemaVersion = 1
        mode          = $mode
        generatedBy   = 'tools/build-profiles.ps1'
        label         = $manual.$mode.label
        intent        = $manual.$mode.intent
        power         = $manual.$mode.power
        wsl           = $manual.$mode.wsl
        apps          = [ordered]@{
            close            = @($manual.$mode.apps.close)
            launch           = @($manual.$mode.apps.launch)
            keepOpen         = @($manual.$mode.apps.keepOpen)
            note             = 'Only apps the user listed here are closed or launched. Protected apps are never uninstalled or blocked.'
            suggestedToClose = $startupCandidates
        }
        services      = [ordered]@{ stop = $stop; ensureRunning = $ensure }
        tweaks        = $modeTweaks
    }
    $modeProfile | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $profilesDir "$mode.json")
    '{0,-5} stop={1,3} ensure={2,2} tweaks={3,2} suggestedApps={4,2}' -f $mode, $stop.Count, $ensure.Count, $modeTweaks.Count, $startupCandidates.Count
}
'baseline tweaks={0}' -f $baselineTweaks.Count

