#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$taskRoot = Join-Path $repositoryRoot 'outputs/software-publisher-icons'
$assetDirectory = Join-Path $repositoryRoot 'src/WinModes.App/Assets/Software'
$sourcesPath = Join-Path $assetDirectory 'sources.json'
$sources = Get-Content -LiteralPath $sourcesPath -Raw | ConvertFrom-Json -AsHashtable
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$apps = @(
    @{Id='process-explorer'; Archive='ProcessExplorer'; Exe='procexp64.exe'},
    @{Id='process-monitor'; Archive='ProcessMonitor'; Exe='Procmon64.exe'}
)
foreach ($app in $apps) {
    $zip = Join-Path $taskRoot ($app.Archive + '.zip')
    $directory = Join-Path $taskRoot $app.Archive
    $assetUrl = 'https://download.sysinternals.com/files/' + $app.Archive + '.zip'
    Invoke-WebRequest -Uri $assetUrl -OutFile $zip -TimeoutSec 30
    Expand-Archive -LiteralPath $zip -DestinationPath $directory -Force
    $executable = Join-Path $directory $app.Exe
    $signature = Get-AuthenticodeSignature -LiteralPath $executable
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Publisher signature was not verified.' }
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($executable)
    try {
        $bitmap = $icon.ToBitmap()
        try { $bitmap.Save((Join-Path $assetDirectory ($app.Id + '.png')), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $bitmap.Dispose() }
    } finally { $icon.Dispose() }
    'Extracted signed publisher icon: ' + $app.Id
    $sources[$app.Id] = @{website=('https://learn.microsoft.com/sysinternals/downloads/' + $(if($app.Id -eq 'process-monitor'){'procmon'}else{$app.Id})); asset=$assetUrl; extraction='Icon resource from a Microsoft-signed executable; no executable was run.'}
}
$sources | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $sourcesPath -Encoding utf8
