# Pester tests for Release.psm1. Run with:  pwsh -c "Invoke-Pester scripts/release/tests"

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'Release.psm1') -Force
}

Describe 'Get-ChangeBump' {
    It 'defaults to patch, never none' {
        (Get-ChangeBump -Labels @()).Bump | Should -Be 'patch'
        (Get-ChangeBump).Bump | Should -Be 'patch'
    }
    It 'is minor when the PR adds an announcement' {
        (Get-ChangeBump -AddsAnnouncement).Bump | Should -Be 'minor'
    }
    It 'lets a label win over the announcement rule' {
        (Get-ChangeBump -Labels @('semver:patch') -AddsAnnouncement).Bump | Should -Be 'patch'
    }
    It 'takes the biggest of several semver labels' {
        (Get-ChangeBump -Labels @('semver:patch', 'semver:major', 'semver:minor')).Bump | Should -Be 'major'
    }
    It 'ignores unrelated labels' {
        (Get-ChangeBump -Labels @('dependencies', 'deploy:manual', 'semver:bogus')).Bump | Should -Be 'patch'
    }
    It 'never makes major without a label' {
        (Get-ChangeBump -AddsAnnouncement).Bump | Should -Not -Be 'major'
    }
}

Describe 'Test-AddsAnnouncement' {
    BeforeAll {
        $script:one = '{"features":[{"pr":7,"date":"2026-10-01","title":"A","description":"a"}]}'
        $script:two = '{"features":[{"pr":9,"date":"2026-10-02","title":"B","description":"b"},{"pr":7,"date":"2026-10-01","title":"A","description":"a"}]}'
        $script:typo = '{"features":[{"pr":7,"date":"2026-10-01","title":"A!","description":"fixed"}]}'
    }
    It 'is true for a new pr entry' { Test-AddsAnnouncement -Before $one -After $two | Should -BeTrue }
    It 'is true when the file is created with an entry' { Test-AddsAnnouncement -Before '' -After $one | Should -BeTrue }
    It 'is false when an existing entry is edited' { Test-AddsAnnouncement -Before $one -After $typo | Should -BeFalse }
    It 'is false when the file is created empty' { Test-AddsAnnouncement -Before $null -After '{"features": []}' | Should -BeFalse }
    It 'is false when an entry is removed' { Test-AddsAnnouncement -Before $two -After $one | Should -BeFalse }
    It 'is false for invalid JSON' { Test-AddsAnnouncement -Before $one -After '{"features": [' | Should -BeFalse }
}

Describe 'Step-SemVer' {
    It 'bumps each part and resets the lower ones' -ForEach @(
        @{ v = '1.0.3'; b = 'patch'; e = '1.0.4' }
        @{ v = 'v1.0.3'; b = 'minor'; e = '1.1.0' }
        @{ v = '1.9.3'; b = 'major'; e = '2.0.0' }
        @{ v = '0.1.9'; b = 'patch'; e = '0.1.10' }
    ) {
        Step-SemVer -Version $v -Bump $b | Should -Be $e
    }
    It 'starts at 1.0.0 with no previous version' {
        Step-SemVer -Version '' -Bump 'patch' | Should -Be '1.0.0'
    }
    It 'rejects a non-SemVer version' {
        { Step-SemVer -Version '1.0' -Bump 'patch' } | Should -Throw
    }
}

Describe 'Test-BookkeepingCommit' {
    It 'recognises the release job commit' {
        Test-BookkeepingCommit -Message 'Release v1.1.0: changelog and version' -Files @('CHANGELOG.md', 'src/MultiBox.App/MultiBox.App.csproj') | Should -BeTrue
    }
    It 'is false when the commit touches anything else' {
        Test-BookkeepingCommit -Message 'Release v1.1.0' -Files @('CHANGELOG.md', 'src/MultiBox.Core/X.cs') | Should -BeFalse
    }
    It 'is false for an ordinary message touching the changelog' {
        Test-BookkeepingCommit -Message 'Fix typo in changelog' -Files @('CHANGELOG.md') | Should -BeFalse
    }
    It 'is false with no files' {
        Test-BookkeepingCommit -Message 'Release v1.1.0' -Files @() | Should -BeFalse
    }
}

Describe 'Get-NextRelease' {
    It 'takes the biggest bump among several merges' {
        $r = Get-NextRelease -LatestTag 'v1.0.3' -Changes @(
            [pscustomobject]@{ Number = 15; Labels = @('dependencies'); Files = @('src/MultiBox.App/MultiBox.App.csproj') }
            [pscustomobject]@{ Number = 16; Labels = @(); Files = @('whats-new/features.json'); AddsAnnouncement = $true }
        )
        $r.Release | Should -BeTrue
        $r.Bump | Should -Be 'minor'
        $r.Version | Should -Be '1.1.0'
        $r.Tag | Should -Be 'v1.1.0'
        $r.Reason | Should -Match '#15=patch'
        $r.Reason | Should -Match '#16=minor'
    }
    It 'releases a docs-only change as a patch (no skip)' {
        $r = Get-NextRelease -LatestTag 'v2.4.0' -Changes @([pscustomobject]@{ Number = 3; Labels = @(); Files = @('docs/USER-GUIDE.md') })
        $r.Version | Should -Be '2.4.1'
    }
    It 'treats a direct push with no PR as a patch' {
        $r = Get-NextRelease -LatestTag 'v1.0.0' -Changes @([pscustomobject]@{ Labels = @(); Files = @('README.md'); Message = 'Fix README' })
        $r.Bump | Should -Be 'patch'
        $r.Reason | Should -Match 'direct push'
    }
    It 'has nothing to release when only the bookkeeping commit is new' {
        $r = Get-NextRelease -LatestTag 'v1.1.0' -Changes @(
            [pscustomobject]@{ Labels = @(); Files = @('CHANGELOG.md'); Message = 'Release v1.1.0' })
        $r.Release | Should -BeFalse
        $r.Version | Should -Be '1.1.0'
    }
    It 'has nothing to release with no changes' {
        (Get-NextRelease -LatestTag 'v1.1.0' -Changes @()).Release | Should -BeFalse
    }
    It 'makes the first release 1.0.0' {
        (Get-NextRelease -LatestTag '' -Changes @([pscustomobject]@{ Labels = @(); Files = @('a') })).Version | Should -Be '1.0.0'
    }
    It 'marks the release as prerelease when any PR holds the deploy' {
        $r = Get-NextRelease -LatestTag 'v1.0.0' -Changes @(
            [pscustomobject]@{ Number = 1; Labels = @(); Files = @('a') }
            [pscustomobject]@{ Number = 2; Labels = @('deploy:manual'); Files = @('b') })
        $r.Prerelease | Should -BeTrue
        $r.Bump | Should -Be 'patch'
    }
}

Describe 'Test-Announcements' {
    It 'accepts an empty list' {
        @(Test-Announcements -Json '{"features": []}').Count | Should -Be 0
    }
    It 'accepts a complete entry' {
        $json = '{"features":[{"pr":42,"date":"2026-10-17","title":"Export","description":"Does a thing.\n\nOn the header.","images":[{"src":"whats-new/img/pr-42-x.png","caption":"Header"}]}]}'
        @(Test-Announcements -Json $json).Count | Should -Be 0
    }
    It 'rejects invalid JSON' {
        @(Test-Announcements -Json '{"features": [').Count | Should -Be 1
        Test-Announcements -Json '{"features": [' | Should -Match 'not valid JSON'
    }
    It 'rejects a missing features array' {
        Test-Announcements -Json '{"items": []}' | Should -Match 'features'
    }
    It 'reports missing fields, bad dates, HTML and duplicate PRs' {
        $json = '{"features":[{"pr":1,"date":"17/10/2026","title":"A","description":"<b>x</b>"},{"pr":1,"title":"","description":"y","date":"2026-10-17"},{"pr":"3","date":"2026-10-17","title":"C","description":"z","images":[{"src":""}]}]}'
        $e = Test-Announcements -Json $json
        $e | Should -Contain 'features[0]: date must be YYYY-MM-DD'
        $e | Should -Contain 'features[0]: description must be plain text (no HTML)'
        $e | Should -Contain 'features[1]: missing title'
        $e | Should -Contain 'features[1]: pr 1 is not unique'
        $e | Should -Contain 'features[2]: pr must be an integer'
        $e | Should -Contain 'features[2].images[0]: missing src'
        $e | Should -Contain 'features[2].images[0]: missing caption'
    }
}

Describe 'Get-ChangeLogRows' {
    It 'parses index rows and skips the header' {
        $index = @'
# Release log

| Date | Change | Summary |
| --- | --- | --- |
| 2026-10-18 | [Ship-change process](2026-10-18-ship-change-process/README.md) | Internal: every merge releases |
| YYYY-MM-DD | Change name | No link, so not a real row |
'@
        $rows = @(Get-ChangeLogRows -IndexContent $index)
        $rows.Count | Should -Be 1
        $rows[0].Folder | Should -Be '2026-10-18-ship-change-process'
        $rows[0].Name | Should -Be 'Ship-change process'
        $rows[0].Summary | Should -Be 'Internal: every merge releases'
    }
}

Describe 'New-ReleaseNotes' {
    It 'lists announcements, entries and PRs' {
        $n = New-ReleaseNotes `
            -Announcements @([pscustomobject]@{ pr = 42; title = 'Export'; description = "One.`n`nTwo." }) `
            -Entries @([pscustomobject]@{ Name = 'Export'; Folder = '2026-10-17-export'; Summary = 'Adds export' }) `
            -PullRequests @([pscustomobject]@{ Number = 42; Title = 'Add export' })
        $n | Should -Match '### New'
        $n | Should -Match '\*\*Export\.\*\* One\. Two\. \(#42\)'
        $n | Should -Match 'changelog/2026-10-17-export/README\.md\): Adds export'
        $n | Should -Match '- Add export \(#42\)'
    }
    It 'still says something for an empty release' {
        New-ReleaseNotes | Should -Match 'Maintenance release'
    }
}

Describe 'Update-Changelog' {
    BeforeAll {
        $script:legacy = @'
# Changelog

Intro.

## [Unreleased]

### Added

- Settings sync.

## [1.0.3] - 2026-09-24

### Added

- Version in header.
'@
    }
    It 'inserts the version above the newest release and folds Unreleased into it' {
        $out = Update-Changelog -Content $legacy -Version '1.1.0' -Date '2026-10-18' -Notes "### Changes`n`n- A thing"
        $out | Should -Not -Match '\[Unreleased\]'
        $out.IndexOf('## [1.1.0] - 2026-10-18') | Should -BeLessThan $out.IndexOf('## [1.0.3]')
        $out.IndexOf('Intro.') | Should -BeLessThan $out.IndexOf('## [1.1.0]')
        $out | Should -Match '### Earlier, unversioned changes'
        $out | Should -Match '#### Added'
        $out | Should -Match '- Settings sync\.'
        $out | Should -Match '- Version in header\.'
    }
    It 'works without an Unreleased section' {
        $c = "# Changelog`n`n## [1.0.0] - 2026-01-01`n`n- First`n"
        $out = Update-Changelog -Content $c -Version '1.0.1' -Date '2026-02-01' -Notes '- Fix'
        $out | Should -Match "(?s)## \[1\.0\.1\] - 2026-02-01\n\n- Fix\n\n## \[1\.0\.0\]"
        $out | Should -Not -Match 'Earlier, unversioned'
    }
    It 'appends when there is no release yet' {
        $out = Update-Changelog -Content "# Changelog`n" -Version '1.0.0' -Date '2026-02-01' -Notes '- First'
        $out | Should -Match '## \[1\.0\.0\] - 2026-02-01'
    }
    It 'is idempotent for a version already present' {
        $once = Update-Changelog -Content $legacy -Version '1.1.0' -Date '2026-10-18' -Notes '- A'
        Update-Changelog -Content $once -Version '1.1.0' -Date '2026-10-19' -Notes '- B' | Should -Be $once
    }
    It 'keeps CRLF line endings' {
        $c = "# Changelog`r`n`r`n## [1.0.0] - 2026-01-01`r`n"
        $out = Update-Changelog -Content $c -Version '1.0.1' -Date '2026-02-01' -Notes '- Fix'
        $out | Should -Match "## \[1\.0\.1\] - 2026-02-01`r`n"
        $out | Should -Not -Match "[^`r]`n"
    }
}

Describe 'Set-ProjectVersion' {
    It 'replaces the version' {
        Set-ProjectVersion -Content '<Project><PropertyGroup><Version>1.0.3</Version></PropertyGroup></Project>' -Version '1.1.0' |
            Should -Be '<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>'
    }
    It 'throws without a Version property' {
        { Set-ProjectVersion -Content '<Project/>' -Version '1.1.0' } | Should -Throw
    }
}
