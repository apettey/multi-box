# Integration tests for next-version.ps1: a throwaway git repo, and a `gh` function standing in
# for the GitHub CLI (functions win over executables, and the script sees the caller's scope).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidGlobalVars', '', Justification = 'The gh stand-in is read from the script under test, which only sees global state.')]
param()

BeforeAll {
    $script:NextVersion = Join-Path $PSScriptRoot '..' 'next-version.ps1'
    $script:Repo = Join-Path ([IO.Path]::GetTempPath()) "nv-test-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $Repo | Out-Null
    Push-Location $Repo

    function script:Commit([string]$Message, [hashtable]$Files) {
        foreach ($k in $Files.Keys) {
            $dir = Split-Path -Parent $k
            if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
            Set-Content -Path $k -Value $Files[$k] -NoNewline
            git add -- $k
        }
        git commit -q -m $Message
        return (git rev-parse HEAD)
    }

    git init -q -b main
    git config user.email t@example.com
    git config user.name t
    Commit 'base' @{ 'README.md' = 'x'; 'whats-new/features.json' = '{"features":[]}' } | Out-Null
    git tag -a v1.0.0 -m v1.0.0   # annotated, like v1.0.1-v1.0.3 in the real repo
    $script:ShaFeature = Commit 'Add feature (#5)' @{ 'whats-new/features.json' = '{"features":[{"pr":5,"date":"2026-10-01","title":"F","description":"d"}]}'; 'src/a.cs' = 'a' }
    $script:ShaDocs = Commit 'Docs (#6)' @{ 'docs/x.md' = 'docs' }

    $global:GhPulls = @{
        $ShaFeature = '[{"number":5,"title":"Add feature","merge_commit_sha":"' + $ShaFeature + '","labels":[]}]'
        $ShaDocs    = '[{"number":6,"title":"Docs","merge_commit_sha":"' + $ShaDocs + '","labels":[{"name":"deploy:manual"}]}]'
    }
    $global:GhReleases = @()
    $global:GhFail = $false
    $global:GhPr = '{"number":7,"title":"Preview me","labels":[{"name":"semver:major"}]}'

    function global:gh {
        $global:LASTEXITCODE = 0
        if ($args[0] -eq 'api' -and $args[1] -match '/commits/([0-9a-f]+)/pulls$') {
            if ($global:GhFail) { $global:LASTEXITCODE = 1; return }
            $j = $global:GhPulls[$Matches[1]]
            if ($j) { return $j } else { return '[]' }
        }
        if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
            if ($global:GhReleases -notcontains $args[2]) { $global:LASTEXITCODE = 1 }
            return
        }
        if ($args[0] -eq 'pr' -and $args[1] -eq 'view') { return $global:GhPr }
        throw "unexpected gh call: $args"
    }
}

AfterAll {
    Pop-Location
    Remove-Item -Recurse -Force $Repo -ErrorAction SilentlyContinue
    Remove-Item -Path 'Function:\gh' -Force -ErrorAction SilentlyContinue
}

Describe 'next-version.ps1' {
    BeforeEach { $global:GhFail = $false; $global:GhReleases = @() }

    It 'computes the bump from the merged PRs since the last tag' {
        $r = & $NextVersion -Ref main -Repo o/r
        $r.Release | Should -BeTrue
        $r.Version | Should -Be '1.1.0'           # #5 added an announcement
        $r.PreviousTag | Should -Be 'v1.0.0'
        $r.Prerelease | Should -BeTrue            # #6 holds the deploy
        @($r.PullRequests.Number) | Should -Be @(5, 6)
        $r.Finalize | Should -BeTrue
    }

    It 'fails closed when the PR lookup errors' {
        $global:GhFail = $true
        { & $NextVersion -Ref main -Repo o/r } | Should -Throw '*Could not look up the PR*'
    }

    It 'reuses the tag on an already-tagged commit with no Release yet' {
        git tag v1.1.0 $ShaDocs
        try {
            $r = & $NextVersion -Ref $ShaDocs -Repo o/r
            $r.Release | Should -BeTrue
            $r.Tag | Should -Be 'v1.1.0'
            $r.PreviousTag | Should -Be 'v1.0.0'
        } finally { git tag -d v1.1.0 | Out-Null }
    }

    It 'skips the build but still finalizes when the Release exists' {
        git tag v1.1.0 $ShaDocs
        $global:GhReleases = @('v1.1.0')
        try {
            $r = & $NextVersion -Ref $ShaDocs -Repo o/r
            $r.Release | Should -BeFalse
            $r.Finalize | Should -BeTrue
        } finally { git tag -d v1.1.0 | Out-Null }
    }

    It 'ignores the bookkeeping commit after a release' {
        git tag v1.1.0 $ShaDocs
        git checkout -q -b bk $ShaDocs
        try {
            Commit 'Release v1.1.0: changelog and version' @{ 'CHANGELOG.md' = '## [1.1.0]' } | Out-Null
            $r = & $NextVersion -Ref HEAD -Repo o/r
            $r.Release | Should -BeFalse
            $r.Finalize | Should -BeFalse
            $r.Reason | Should -Be 'no unreleased changes'
        } finally { git checkout -q main; git branch -q -D bk; git tag -d v1.1.0 | Out-Null }
    }

    It 'previews a PR on top of the unreleased changes on the base branch' {
        git checkout -q -b feature main
        try {
            Commit 'PR change' @{ 'src/b.cs' = 'b' } | Out-Null
            $r = & $NextVersion -PullRequest 7 -BaseRef main -Repo o/r
            $r.Version | Should -Be '2.0.0'       # semver:major on the PR wins
            $r.Reason | Should -Match '#7=major'
            $r.Finalize | Should -BeTrue
        } finally { git checkout -q main; git branch -q -D feature }
    }
}
