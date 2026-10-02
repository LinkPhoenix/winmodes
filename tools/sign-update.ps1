#Requires -Version 7.0
<#
.SYNOPSIS
    Signs the SHA256SUMS.txt of a release with the private update key and checks the result with the public key.
.DESCRIPTION
    Writes <file>.sig next to the file: the ECDSA P-256 / SHA-256 signature, base64 (IEEE P1363 format), which the app checks with
    the public key built into it (src/WinModes.Core/Updates/update-public-key.pem). The private key is read from the environment
    variable WINMODES_UPDATE_KEY (PEM text, as set from the GitHub secret) or from -PrivateKeyPath; it is never printed.
.EXAMPLE
    pwsh -NoProfile -File tools/sign-update.ps1 -Path artifacts/SHA256SUMS.txt -PrivateKeyPath "$env:USERPROFILE\.winmodes\update-signing-key.pem"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [string]$PrivateKeyPath,
    [string]$PublicKeyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/WinModes.Core/Updates/update-public-key.pem')
)

$ErrorActionPreference = 'Stop'
$pem = if ($PrivateKeyPath) { Get-Content -LiteralPath $PrivateKeyPath -Raw } else { $env:WINMODES_UPDATE_KEY }
if ([string]::IsNullOrWhiteSpace($pem)) { throw 'No private key: set WINMODES_UPDATE_KEY or pass -PrivateKeyPath.' }

$data = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path))
$format = [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation
$hash = [System.Security.Cryptography.HashAlgorithmName]::SHA256

$signer = [System.Security.Cryptography.ECDsa]::Create()
try {
    $signer.ImportFromPem($pem)
    $signature = $signer.SignData($data, $hash, $format)
}
finally { $signer.Dispose() }

# The signature is checked at once with the public key that the app carries: a wrong secret fails here, not on users' PCs.
if (Test-Path -LiteralPath $PublicKeyPath) {
    $verifier = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $verifier.ImportFromPem((Get-Content -LiteralPath $PublicKeyPath -Raw))
        if (-not $verifier.VerifyData($data, $signature, $hash, $format)) { throw "The signature does not match ${PublicKeyPath}: the private key is not the one of this app." }
    }
    finally { $verifier.Dispose() }
}

$output = "$Path.sig"
Set-Content -LiteralPath $output -Value ([Convert]::ToBase64String($signature)) -NoNewline
"Signed $Path -> $output"
