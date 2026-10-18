<#
.SYNOPSIS
    Works out the version the next release gets (every change releases; see AGENTS.md).

.DESCRIPTION
    Collects the unreleased changes - first-parent commits since the latest vX.Y.Z tag, each
    with its merged PR's number, title and labels - and asks Release.psm1 for the bump.

    On main (default):   changes in <latest tag>..<Ref>.
    With -PullRequest N: the unreleased changes already on the base branch plus PR N, to
                         preview "merging this PR would release vX.Y.Z".

    If <Ref> already carries a version tag (a re-run, or finalize after the build), that tag
    is the version and the changes are those since the tag before it. When the release for
    that tag already exists, Release is false unless -IgnoreExistingRelease; Finalize stays
    true so the notes and CHANGELOG.md are (re)written - finalize is idempotent.

    Writes key=value lines to $GITHUB_OUTPUT when set, and returns the result object.

.EXAMPLE
    pwsh scripts/release/next-version.ps1
    pwsh scripts/release/next-version.ps1 -PullRequest 17 -BaseRef origin/main
#>
[CmdletBinding()]
param(
    [string]$Ref = 'HEAD',
    [int]$PullRequest = 0,
    [string]$BaseRef = 'origin/main',
    [string]$Repo = $env:GITHUB_REPOSITORY,
    [switch]$IgnoreExistingRelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
if (-not $Repo) { $Repo = (gh repo view --json nameWithOwner --jq .nameWithOwner) }

function Invoke-Git {
    $out = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed" }
    return $out
}

function Get-VersionTags([string]$At) {
    # Newest first; only plain vX.Y.Z tags reachable from $At.
    @(Invoke-Git tag --merged $At --list 'v*' --sort=-v:refname | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' })
}

function Get-FileAt([string]$At, [string]$Path) {
    if (-not $At) { return '' }
    $text = (git show "${At}:$Path" 2>$null) -join "`n"
    $global:LASTEXITCODE = 0
    return $text
}

function Test-ChangeAddsAnnouncement([string]$Before, [string]$After, [string[]]$Files) {
    if ($Files -notcontains 'whats-new/features.json') { return $false }
    return Test-AddsAnnouncement -Before (Get-FileAt $Before 'whats-new/features.json') -After (Get-FileAt $After 'whats-new/features.json')
}

function Get-CommitChange([string]$Sha) {
    $message = (Invoke-Git log -1 --format=%s $Sha) -join ''
    $parents = @((Invoke-Git log -1 --format=%P $Sha) -split ' ' | Where-Object { $_ })
    $files = if ($parents.Count -gt 0) {
        @(Invoke-Git diff --name-only "$($parents[0])" $Sha)
    } else {
        @(Invoke-Git show --name-only --format= $Sha)
    }
    # Fail closed: a lookup error must not turn a deploy:manual or semver:* PR into an
    # unlabelled patch that auto-updates every installed copy. No PR is an empty list, not an error.
    $raw = gh api "repos/$Repo/commits/$Sha/pulls"
    if ($LASTEXITCODE -ne 0) { throw "Could not look up the PR for $Sha (gh api exit $LASTEXITCODE); not guessing its labels." }
    $pulls = @(($raw -join "`n") | ConvertFrom-Json)
    $pr = $null
    if ($pulls.Count) {
        $pr = @($pulls | Where-Object { $_.merge_commit_sha -eq $Sha }) + $pulls | Select-Object -First 1
    }
    $parent = if ($parents.Count -gt 0) { $parents[0] } else { '' }
    [pscustomobject]@{
        Sha     = $Sha
        Message = $message
        Files   = $files
        AddsAnnouncement = (Test-ChangeAddsAnnouncement $parent $Sha $files)
        Number  = if ($pr) { [int]$pr.number } else { 0 }
        Title   = if ($pr) { [string]$pr.title } else { $message }
        Labels  = if ($pr) { @($pr.labels | ForEach-Object { $_.name }) } else { @() }
    }
}

function Get-ChangesSince([string]$From, [string]$To) {
    $range = if ($From) { "$From..$To" } else { $To }
    @(Invoke-Git log --first-parent --format=%H $range | Where-Object { $_ } | ForEach-Object { Get-CommitChange $_ })
}

$headSha = (Invoke-Git rev-parse $Ref) -join ''
$onHead = @()

if ($PullRequest -gt 0) {
    $tags = @(Get-VersionTags $BaseRef)
    $latest = if ($tags.Count) { $tags[0] } else { '' }
    $changes = @(Get-ChangesSince $latest $BaseRef)
    $view = gh pr view $PullRequest --repo $Repo --json number,title,labels | ConvertFrom-Json
    $prFiles = @(Invoke-Git diff --name-only "$BaseRef...$headSha")
    $mergeBase = (Invoke-Git merge-base $BaseRef $headSha) -join ''
    $changes += [pscustomobject]@{
        Sha = $headSha; Message = $view.title; Files = $prFiles
        AddsAnnouncement = (Test-ChangeAddsAnnouncement $mergeBase $headSha $prFiles)
        Number = [int]$view.number; Title = $view.title
        Labels = @($view.labels | ForEach-Object { $_.name })
    }
    $result = Get-NextRelease -LatestTag $latest -Changes $changes
    $previous = $latest
} else {
    $onHead = @(Invoke-Git tag --points-at $headSha | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Sort-Object { [version]($_ -replace '^v') } -Descending)
    $previousTags = @(Get-VersionTags $headSha | Where-Object { $onHead -notcontains $_ })
    $previous = if ($previousTags.Count) { $previousTags[0] } else { '' }
    $changes = @(Get-ChangesSince $previous $headSha)
    $result = Get-NextRelease -LatestTag $previous -Changes $changes
    if ($onHead.Count) {
        # Already tagged: reuse the tag rather than computing a second version for one commit.
        $tag = $onHead[0]
        $result.Release = $true; $result.Tag = $tag; $result.Version = $tag -replace '^v'
        $result.Reason = "re-run: $headSha is already tagged $tag"
        if (-not $IgnoreExistingRelease) {
            gh release view $tag --repo $Repo *> $null
            if ($LASTEXITCODE -eq 0) { $result.Release = $false; $result.Reason = "$tag is already released" }
        }
        $global:LASTEXITCODE = 0
    }
}

$prs = @($changes | Where-Object { $_.Number -gt 0 } | Sort-Object Number -Unique |
    ForEach-Object { [pscustomobject]@{ Number = $_.Number; Title = $_.Title } })
$result | Add-Member -NotePropertyName Finalize -NotePropertyValue ([bool]($result.Release -or $onHead.Count))
$result | Add-Member -NotePropertyName PreviousTag -NotePropertyValue $previous
$result | Add-Member -NotePropertyName Sha -NotePropertyValue $headSha
$result | Add-Member -NotePropertyName PullRequests -NotePropertyValue $prs

if ($env:GITHUB_OUTPUT) {
    @(
        "release=$($result.Release.ToString().ToLower())"
        "finalize=$($result.Finalize.ToString().ToLower())"
        "version=$($result.Version)"
        "tag=$($result.Tag)"
        "bump=$($result.Bump)"
        "prerelease=$($result.Prerelease.ToString().ToLower())"
        "previous-tag=$previous"
        "sha=$headSha"
        "reason=$($result.Reason -replace '[\r\n]+', ' ')"
    ) | Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8
}

$result
