#Requires -Version 7.0
<#
.SYNOPSIS
    Cuts a release: checks the tree, runs the tests, updates the version and the changelog, commits, tags and pushes.
.DESCRIPTION
    Pushing the tag starts .github/workflows/release.yml, which builds the package and publishes the GitHub release.
    The "Unreleased" section of CHANGELOG.md becomes the section of the new version; when it is empty,
    the commits since the previous tag are listed instead.
.EXAMPLE
    pwsh -NoProfile -File tools/release.ps1 -Version 0.3.0
.EXAMPLE
    pwsh -NoProfile -File tools/release.ps1 -Bump minor -DryRun
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('major', 'minor', 'patch')][string]$Bump,
    # Show what would change without writing, committing or pushing.
    [switch]$DryRun,
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$projectFile = Join-Path $Root 'src/WinModes.App/WinModes.App.csproj'
$changelogFile = Join-Path $Root 'CHANGELOG.md'

function Invoke-Git { git -C $Root @args; if ($LASTEXITCODE -ne 0) { throw "git $args failed." } }

$project = Get-Content -LiteralPath $projectFile -Raw
$current = [regex]::Match($project, '<Version>(\d+)\.(\d+)\.(\d+)</Version>')
if (-not $current.Success) { throw "No <Version> found in $projectFile." }

if (-not $Version) {
    if (-not $Bump) { throw 'Give -Version X.Y.Z or -Bump major|minor|patch.' }
    $major, $minor, $patch = $current.Groups[1..3].Value | ForEach-Object { [int]$_ }
    $Version = switch ($Bump) {
        'major' { "$($major + 1).0.0" }
        'minor' { "$major.$($minor + 1).0" }
        'patch' { "$major.$minor.$($patch + 1)" }
    }
}
$tag = "v$Version"

# Preconditions: a release must come from a clean, up-to-date main branch.
if ((Invoke-Git branch --show-current) -ne 'main') { throw 'Releases are cut from the main branch.' }
if (Invoke-Git status --porcelain) { throw 'The working tree has uncommitted changes. Commit or stash them first.' }
if (Invoke-Git tag --list $tag) { throw "Tag $tag already exists." }
Invoke-Git fetch --quiet origin main
if ((Invoke-Git rev-list --count 'HEAD..origin/main') -ne '0') { throw 'main is behind origin/main. Pull first.' }

# Changelog: move "Unreleased" under the new version.
$changelog = (Get-Content -LiteralPath $changelogFile -Raw).Replace("`r`n", "`n")
$unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\]\n(.*?)(?=^## \[)')
if (-not $unreleased.Success) { throw 'CHANGELOG.md has no "## [Unreleased]" section.' }
$notes = $unreleased.Groups[1].Value.Trim()
if (-not $notes) {
    $previous = git -C $Root describe --tags --abbrev=0 2>$null
    $range = $previous ? "$previous..HEAD" : 'HEAD'
    $notes = "### Changed`n`n" + ((Invoke-Git log $range --no-merges --pretty=format:'- %s') -join "`n")
}
$date = Get-Date -Format 'yyyy-MM-dd'
$newChangelog = $changelog.Remove($unreleased.Index, $unreleased.Length).Insert(
    $unreleased.Index, "## [Unreleased]`n`n## [$Version] - $date`n`n$notes`n`n")
# Keep the comparison links at the bottom of the changelog in step with the new tag.
$repository = 'https://github.com/LinkPhoenix/winmodes'
$link = [regex]::Match($newChangelog, '(?m)^\[Unreleased\]: .*$')
if ($link.Success) {
    $previousTag = [regex]::Match($link.Value, 'compare/(v[\d.]+)\.\.\.HEAD').Groups[1].Value
    $versionLink = $previousTag ? "$repository/compare/$previousTag...$tag" : "$repository/releases/tag/$tag"
    $newChangelog = $newChangelog.Remove($link.Index, $link.Length).Insert(
        $link.Index, "[Unreleased]: $repository/compare/$tag...HEAD`n[$Version]: $versionLink")
}
$newProject = $project.Replace($current.Value, "<Version>$Version</Version>")

"Release $tag (current version $($current.Groups[1].Value).$($current.Groups[2].Value).$($current.Groups[3].Value))"
"--- Release notes ---"
$notes
if ($DryRun) {
    "--- Dry run: nothing was written, committed or pushed. ---"
    return
}

# Every text of the interface must have its translation in each language.
& (Join-Path $PSScriptRoot 'extract-texts.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Translations are missing or unused; the release was not created.' }

dotnet test (Join-Path $Root 'WinModes.slnx') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; the release was not created.' }

Set-Content -LiteralPath $projectFile -Value $newProject -NoNewline
Set-Content -LiteralPath $changelogFile -Value $newChangelog -NoNewline
Invoke-Git add -- $projectFile $changelogFile
Invoke-Git commit --quiet -m "Release $tag"
Invoke-Git tag -a $tag -m "WinModes $tag"
Invoke-Git push --quiet origin main
Invoke-Git push --quiet origin $tag

"Pushed $tag. The release workflow is now building it:"
"https://github.com/LinkPhoenix/winmodes/actions/workflows/release.yml"
