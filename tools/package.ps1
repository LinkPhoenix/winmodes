#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the release packages: a portable zip and, when Inno Setup is installed, the installer.
.DESCRIPTION
    Publishes the app self-contained for win-x64 (no .NET install needed), adds the data the app and the
    elevated helper read next to the executable, then writes into the output folder:
      WinModes-v<version>-portable-win-x64.zip   unzip anywhere and run WinModes.exe
      WinModes-v<version>-setup-win-x64.exe      installs into Program Files (needs Inno Setup 6 or 7)
      SHA256SUMS.txt
    Used by .github/workflows/release.yml and usable locally.
.EXAMPLE
    pwsh -NoProfile -File tools/package.ps1 -Version 0.3.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'),
    # Fail instead of skipping the installer when Inno Setup is missing.
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
$appDir = Join-Path $OutputDir 'WinModes'

# Only this script's own previous output is cleared.
foreach ($stale in @($appDir) + @(Get-ChildItem -LiteralPath $OutputDir -Filter 'WinModes-v*' -File -ErrorAction Ignore | ForEach-Object FullName)) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

dotnet publish (Join-Path $Root 'src/WinModes.App') -c Release -r win-x64 --self-contained true -o $appDir --nologo -v q "-p:Version=$Version" -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# The app and the elevated helper look for these next to the executable.
New-Item -ItemType Directory -Force -Path (Join-Path $appDir 'data') | Out-Null
Copy-Item -LiteralPath (Join-Path $Root 'data/protected.json') -Destination (Join-Path $appDir 'data')
# Knowledge base and tweak catalog read by the Optimize page (the helper reads the catalog too).
Copy-Item -LiteralPath (Join-Path $Root 'data/tweaks.json') -Destination (Join-Path $appDir 'data')
Copy-Item -LiteralPath (Join-Path $Root 'data/db') -Destination (Join-Path $appDir 'data/db') -Recurse
Copy-Item -LiteralPath (Join-Path $Root 'profiles') -Destination (Join-Path $appDir 'profiles') -Recurse
foreach ($document in 'LICENSE.md', 'README.md', 'CHANGELOG.md') {
    Copy-Item -LiteralPath (Join-Path $Root $document) -Destination $appDir
}

foreach ($required in 'WinModes.exe', 'WinModes.Elevated.exe', 'data/protected.json', 'data/tweaks.json', 'data/db/windows-services.json', 'profiles/code.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $appDir $required))) { throw "The package is missing $required." }
}

$portable = Join-Path $OutputDir "WinModes-v$Version-portable-win-x64.zip"
Compress-Archive -Path $appDir -DestinationPath $portable
$packages = @($portable)

$compiler = @(
    (Get-Command ISCC.exe -ErrorAction Ignore)?.Source
    foreach ($programs in $env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs')) {
        foreach ($release in 'Inno Setup 7', 'Inno Setup 6') { Join-Path $programs "$release\ISCC.exe" }
    }
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if ($compiler) {
    & $compiler /Qp "/DAppVersion=$Version" "/DSourceDir=$appDir" "/DOutputDir=$OutputDir" (Join-Path $Root 'installer/WinModes.iss')
    if ($LASTEXITCODE -ne 0) { throw 'The installer could not be compiled.' }
    $packages += Join-Path $OutputDir "WinModes-v$Version-setup-win-x64.exe"
}
elseif ($RequireInstaller) {
    throw 'Inno Setup (ISCC.exe) was not found.'
}
else {
    Write-Warning 'Inno Setup (ISCC.exe) was not found: only the portable zip was built.'
}

$sums = $packages | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $_) }
Set-Content -LiteralPath (Join-Path $OutputDir 'SHA256SUMS.txt') -Value $sums

$packages + (Join-Path $OutputDir 'SHA256SUMS.txt') | ForEach-Object {
    '{0}  ({1:0.0} MB)' -f $_, ((Get-Item -LiteralPath $_).Length / 1MB)
}
