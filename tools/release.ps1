#Requires -Version 7.0
<#
.SYNOPSIS
    Cuts a release: checks the tree, runs the tests, updates the version and the changelog, commits, tags and pushes.
.DESCRIPTION
    Pushing the tag starts .github/workflows/release.yml, which builds the package and publishes the GitHub release.
    The "Unreleased" section of CHANGELOG.md becomes the section of the new version; when it is empty,
    the commits since the previous tag are listed instead.

    With -Beta the release is a pre-release cut from the beta branch: the tag is vX.Y.Z-beta.YYYYMMDD (with .N added when
    several betas come out the same day), like the preview releases of OpenCodex. Only the version is committed: the
    changelog keeps its "Unreleased" section for the stable release, and stable users are never offered a beta.
.EXAMPLE
    pwsh -NoProfile -File tools/release.ps1 -Version 0.3.0
.EXAMPLE
    pwsh -NoProfile -File tools/release.ps1 -Bump minor -DryRun
.EXAMPLE
    pwsh -NoProfile -File tools/release.ps1 -Beta -DryRun
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-beta\.\d{8}(\.\d{1,2})?)?$')][string]$Version,
    [ValidateSet('major', 'minor', 'patch')][string]$Bump,
    # Cut a beta (pre-release) from the beta branch. Without -Version or -Bump the beta is for the next patch version.
    [switch]$Beta,
    # Show what would change without writing, committing or pushing.
    [switch]$DryRun,
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$projectFile = Join-Path $Root 'src/WinModes.App/WinModes.App.csproj'
$changelogFile = Join-Path $Root 'CHANGELOG.md'

function Invoke-Git { git -C $Root @args; if ($LASTEXITCODE -ne 0) { throw "git $args failed." } }

$project = Get-Content -LiteralPath $projectFile -Raw
$current = [regex]::Match($project, '<Version>(\d+)\.(\d+)\.(\d+)(-beta\.[\d.]+)?</Version>')
if (-not $current.Success) { throw "No <Version> found in $projectFile." }
$currentIsBeta = $current.Groups[4].Success

if (-not $Version) {
    if (-not $Bump -and -not $Beta) { throw 'Give -Version X.Y.Z, -Bump major|minor|patch or -Beta.' }
    $major, $minor, $patch = $current.Groups[1..3].Value | ForEach-Object { [int]$_ }
    # A beta is a step towards its own patch version: the stable release that follows keeps that number.
    $Version = switch ($Bump ? $Bump : 'patch') {
        'major' { "$($major + 1).0.0" }
        'minor' { "$major.$($minor + 1).0" }
        'patch' { $currentIsBeta ? "$major.$minor.$patch" : "$major.$minor.$($patch + 1)" }
    }
}

# Preconditions: a release must come from a clean, up-to-date branch: main, or beta for a beta.
$branch = $Beta ? 'beta' : 'main'
if ((Invoke-Git branch --show-current) -ne $branch) { throw "This release is cut from the $branch branch." }
if (Invoke-Git status --porcelain) { throw 'The working tree has uncommitted changes. Commit or stash them first.' }

if ($Beta -and -not $Version.Contains('-beta.')) {
    # The day, and a revision when there is already a beta tag for that day.
    $day = Get-Date -Format 'yyyyMMdd'
    $stamp = $day
    for ($revision = 2; Invoke-Git tag --list "v$Version-beta.$stamp"; $revision++) { $stamp = "$day.$revision" }
    $Version = "$Version-beta.$stamp"
}
if ($Beta -ne $Version.Contains('-beta.')) { throw 'Use -Beta for a beta version (X.Y.Z-beta.YYYYMMDD), and only for it.' }
$tag = "v$Version"
if (Invoke-Git tag --list $tag) { throw "Tag $tag already exists." }
if (git -C $Root ls-remote --exit-code --heads origin $branch 2>$null) {
    Invoke-Git fetch --quiet origin $branch
    if ((Invoke-Git rev-list --count "HEAD..origin/$branch") -ne '0') { throw "$branch is behind origin/$branch. Pull first." }
}

# Changelog: move "Unreleased" under the new version.
$changelog = (Get-Content -LiteralPath $changelogFile -Raw).Replace("`r`n", "`n")
$unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\]\n(.*?)(?=^## \[)')
if (-not $unreleased.Success) { throw 'CHANGELOG.md has no "## [Unreleased]" section.' }
$notes = $unreleased.Groups[1].Value.Trim()
if (-not $notes -and -not $Beta) {
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

"Release $tag$($Beta ? ' (beta, pre-release)' : '') (current version $($current.Value -replace '</?Version>'))"
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
if ($Beta) {
    # The changelog keeps its Unreleased section: it becomes the notes of the stable release.
    Invoke-Git add -- $projectFile
    # The version may already be in the tree, when the beta branch carries it from the work on the beta tag.
    git -C $Root diff --cached --quiet
    if ($LASTEXITCODE -ne 0) { Invoke-Git commit --quiet -m "Beta $tag" }
}
else {
    Set-Content -LiteralPath $changelogFile -Value $newChangelog -NoNewline
    Invoke-Git add -- $projectFile $changelogFile
    Invoke-Git commit --quiet -m "Release $tag"
}
Invoke-Git tag -a $tag -m "WinModes $tag"
Invoke-Git push --quiet origin $branch
Invoke-Git push --quiet origin $tag

"Pushed $tag. The release workflow is now building it:"
"https://github.com/LinkPhoenix/winmodes/actions/workflows/release.yml"
