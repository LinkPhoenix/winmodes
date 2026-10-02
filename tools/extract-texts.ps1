<#
.SYNOPSIS
Lists every interface text that goes through Loc (the English text is the translation key) and reports the
keys each language file is missing or no longer needs.

.EXAMPLE
pwsh -NoProfile -File tools/extract-texts.ps1            # report
pwsh -NoProfile -File tools/extract-texts.ps1 -List      # print the keys only
#>
param([switch]$List)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Get-SourceFiles([string]$filter) {
    Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter $filter |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
}

# XAML: {l:T 'text'}, where \' stands for an apostrophe.
$markup = [regex]"\{l:T '((?:[^'\\]|\\.)*)'\}"
foreach ($file in Get-SourceFiles '*.xaml') {
    foreach ($match in $markup.Matches([IO.File]::ReadAllText($file.FullName))) {
        $text = $match.Groups[1].Value -replace "\\(.)", '$1'
        [void]$keys.Add([System.Net.WebUtility]::HtmlDecode($text))
    }
}

# C#: every string literal inside the parentheses of Loc.T, Loc.F, Loc.N or Loc.In.
$call = [regex]'Loc\.(T|F|N|In)\('
$literal = [regex]'"((?:[^"\\]|\\.)*)"'
foreach ($file in Get-SourceFiles '*.cs') {
    $source = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in $call.Matches($source)) {
        $depth = 1
        $index = $match.Index + $match.Length
        $start = $index
        while ($index -lt $source.Length -and $depth -gt 0) {
            $char = $source[$index]
            if ($char -eq '"') {
                $index = $literal.Match($source, $index).Index + $literal.Match($source, $index).Length
                continue
            }
            if ($char -eq '(') { $depth++ } elseif ($char -eq ')') { $depth-- }
            $index++
        }
        foreach ($found in $literal.Matches($source.Substring($start, $index - $start))) {
            $text = [regex]::Unescape($found.Groups[1].Value)
            if ($text -match '[A-Za-z]') { [void]$keys.Add($text) }
        }
    }
}

# Texts that come from data and are translated when shown.
$tweaks = Get-Content -LiteralPath (Join-Path $root 'data/tweaks.json') -Raw | ConvertFrom-Json
foreach ($tweak in $tweaks) {
    foreach ($text in $tweak.title, $tweak.description, $tweak.warning, $tweak.category) {
        if ($text) { [void]$keys.Add($text) }
    }
}
$apps = Get-Content -LiteralPath (Join-Path $root 'data/apps.json') -Raw | ConvertFrom-Json
foreach ($app in $apps) {
    foreach ($text in $app.title, $app.why, $app.breaksIfRemoved, $app.category) {
        if ($text) { [void]$keys.Add($text) }
    }
}
foreach ($mode in 'code', 'work', 'game', 'focus', 'eco') {
    $profile = Get-Content -LiteralPath (Join-Path $root "profiles/$mode.json") -Raw | ConvertFrom-Json
    if ($profile.intent) { [void]$keys.Add($profile.intent) }
}
Get-Content -LiteralPath (Join-Path $root 'src/WinModes.Core/Localization/data-texts.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { [void]$keys.Add($_) }

if ($List) {
    $keys | Sort-Object
    return
}

$problems = 0
foreach ($language in 'fr', 'es', 'it') {
    $path = Join-Path $root "src/WinModes.Core/Localization/$language.json"
    $table = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
    $missing = @($keys | Where-Object { -not $table.ContainsKey($_) } | Sort-Object)
    $unused = @($table.Keys | Where-Object { -not $keys.Contains($_) } | Sort-Object)
    "${language}: $($keys.Count) texts, $($missing.Count) missing, $($unused.Count) unused"
    $missing | ForEach-Object { "  missing: $_" }
    $unused | ForEach-Object { "  unused:  $_" }
    $problems += $missing.Count + $unused.Count
}
exit ([Math]::Min($problems, 1))
