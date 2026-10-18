<#
.SYNOPSIS
    After a release is published: writes its notes and records it in CHANGELOG.md and the
    app's <Version>.

.DESCRIPTION
    Run by .github/workflows/release.yml once the build job has published the artefacts
    and created the tag. Notes are built from what went into the release:
      - whats-new/features.json entries for the PRs in it,
      - changelog/<folder>/ entries added since the previous tag,
      - every merged PR.
    Then it commits "Release vX.Y.Z: changelog and version" on top of the latest main and
    pushes it. next-version.ps1 ignores that commit, so it never triggers another bump.

.EXAMPLE
    pwsh scripts/release/finalize.ps1 -Ref <sha> -Tag v1.1.0
    pwsh scripts/release/finalize.ps1 -Ref v1.0.3 -Tag v1.0.3 -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Ref,
    [Parameter(Mandatory)][string]$Tag,
    [string]$Repo = $env:GITHUB_REPOSITORY,
    [string]$Branch = 'main',
    # Print the notes and the new CHANGELOG.md section; change nothing locally or on GitHub.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
if (-not $Repo) { $Repo = (gh repo view --json nameWithOwner --jq .nameWithOwner) }

$ctx = & (Join-Path $PSScriptRoot 'next-version.ps1') -Ref $Ref -Repo $Repo -IgnoreExistingRelease
if ($ctx.Tag -ne $Tag) { throw "$Ref is tagged '$($ctx.Tag)', expected '$Tag'." }
$version = $Tag -replace '^v'

# Release-log entries added since the previous tag (folders, newest first per the index).
$range = if ($ctx.PreviousTag) { @($ctx.PreviousTag, $Ref) } else { @((git hash-object -t tree /dev/null), $Ref) }
$added = @(git diff --name-only --diff-filter=A @range -- changelog/ |
    Where-Object { $_ -match '^changelog/([^/]+)/README\.md$' } | ForEach-Object { $Matches[1] })
$index = (git show "${Ref}:changelog/README.md" 2>$null) -join "`n"
$rows = @(Get-ChangeLogRows -IndexContent $index)
$entries = @($rows | Where-Object { $added -contains $_.Folder })
$entries += @($added | Where-Object { @($rows.Folder) -notcontains $_ } |
    ForEach-Object { [pscustomobject]@{ Name = $_; Folder = $_; Summary = '(no index row)' } })

$prNumbers = @($ctx.PullRequests | ForEach-Object { $_.Number })
$announcements = @()
$featuresJson = (git show "${Ref}:whats-new/features.json" 2>$null) -join "`n"
if ($featuresJson) {
    $announcements = @(($featuresJson | ConvertFrom-Json).features | Where-Object { $prNumbers -contains [int]$_.pr })
}
$global:LASTEXITCODE = 0

$notes = New-ReleaseNotes -Announcements $announcements -Entries $entries -PullRequests $ctx.PullRequests `
    -RepoUrl "https://github.com/$Repo"
if ($DryRun) {
    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $preview = Update-Changelog -Content (Get-Content CHANGELOG.md -Raw) -Version $version -Date $date -Notes $notes
    Write-Host "----- release notes for $Tag -----`n$notes"
    Write-Host '----- CHANGELOG.md (top) -----'
    ($preview -split "`r?`n" | Select-Object -First 40) -join "`n" | Write-Host
    return
}
$notesFile = Join-Path ([IO.Path]::GetTempPath()) "release-notes-$version.md"
Set-Content -Path $notesFile -Value $notes -Encoding utf8 -NoNewline
gh release edit $Tag --repo $Repo --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { throw "Could not set the notes on release $Tag." }
if ($env:GITHUB_STEP_SUMMARY) { "## $Tag`n`n$notes" | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 }

# Bookkeeping commit on top of the latest main. Retries if another merge lands meanwhile.
$csproj = 'src/MultiBox.App/MultiBox.App.csproj'
for ($attempt = 1; $attempt -le 3; $attempt++) {
    git fetch -q origin $Branch
    git checkout -q -B release-bookkeeping "origin/$Branch"
    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $changelog = Get-Content CHANGELOG.md -Raw
    $newChangelog = Update-Changelog -Content $changelog -Version $version -Date $date -Notes $notes
    $proj = Get-Content $csproj -Raw
    $newProj = $proj
    if ($proj -match '<Version>(\d+\.\d+\.\d+)</Version>' -and [version]$Matches[1] -lt [version]$version) {
        $newProj = Set-ProjectVersion -Content $proj -Version $version
    }
    if ($newChangelog -eq $changelog -and $newProj -eq $proj) { Write-Host 'CHANGELOG.md and <Version> already up to date.'; break }
    [IO.File]::WriteAllText((Resolve-Path CHANGELOG.md), $newChangelog)
    [IO.File]::WriteAllText((Resolve-Path $csproj), $newProj)
    git -c user.name='github-actions[bot]' -c user.email='41898282+github-actions[bot]@users.noreply.github.com' `
        commit -q -m "Release ${Tag}: changelog and version" -- CHANGELOG.md $csproj
    if ($LASTEXITCODE -ne 0) { throw 'Bookkeeping commit failed.' }
    git push -q origin "HEAD:$Branch"
    if ($LASTEXITCODE -eq 0) { Write-Host "Recorded $Tag in CHANGELOG.md and $csproj."; break }
    if ($attempt -eq 3) { throw "Could not push the bookkeeping commit for $Tag." }
    Write-Host "Push rejected (main moved); retrying ($attempt/3)."
}
