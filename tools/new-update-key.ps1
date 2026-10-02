#Requires -Version 7.0
<#
.SYNOPSIS
    Creates the key pair that signs the releases of WinModes (ECDSA P-256).
.DESCRIPTION
    The PUBLIC key goes into src/WinModes.Core/Updates/update-public-key.pem: commit it, it is built into the app, which then
    refuses an update whose SHA256SUMS.txt is not signed with the matching private key.
    The PRIVATE key is written to a file OUTSIDE the repository and must never be committed or shared. Store it as the
    GitHub secret WINMODES_UPDATE_KEY so the release workflow signs each release, for example:
        gh secret set WINMODES_UPDATE_KEY --repo LinkPhoenix/winmodes < <private key file>
    Keep a backup of the private key: without it no later release can be accepted by the apps that carry the public key.
    Once the public key is committed, tools/package.ps1 refuses to build a release without the private key.
.EXAMPLE
    pwsh -NoProfile -File tools/new-update-key.ps1 -PrivateKeyPath "$env:USERPROFILE\.winmodes\update-signing-key.pem"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [string]$PublicKeyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/WinModes.Core/Updates/update-public-key.pem'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
foreach ($path in $PrivateKeyPath, $PublicKeyPath) {
    if ((Test-Path -LiteralPath $path) -and -not $Force) { throw "$path already exists. A new key would stop the installed apps from accepting your releases: use -Force only on purpose." }
}

$repository = Split-Path -Parent $PSScriptRoot
if ((Resolve-Path -LiteralPath (Split-Path -Parent $PrivateKeyPath) -ErrorAction Ignore) -and ([IO.Path]::GetFullPath($PrivateKeyPath)).StartsWith([IO.Path]::GetFullPath($repository), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The private key must be stored outside the repository.'
}

$key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $PrivateKeyPath) | Out-Null
    Set-Content -LiteralPath $PrivateKeyPath -Value $key.ExportPkcs8PrivateKeyPem() -NoNewline
    Set-Content -LiteralPath $PublicKeyPath -Value $key.ExportSubjectPublicKeyInfoPem() -NoNewline
}
finally { $key.Dispose() }

"Public key : $PublicKeyPath  (commit it)"
"Private key: $PrivateKeyPath  (keep it secret, back it up, set it as the secret WINMODES_UPDATE_KEY)"
