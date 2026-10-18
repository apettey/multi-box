# Pure release logic: version bumps, announcement validation, changelog generation.
#
# Nothing in this module calls git, gh or the network. The thin scripts next to it
# (next-version.ps1, finalize.ps1, validate-announcements.ps1) gather the inputs and
# write the outputs, so everything that decides a version is covered by
# tests/Release.Tests.ps1. See AGENTS.md section 1 and changelog/README.md.

Set-StrictMode -Version Latest

$script:BumpRank = @{ none = 0; patch = 1; minor = 2; major = 3 }
$script:AnnouncementFile = 'whats-new/features.json'
$script:ChangelogFiles = @('CHANGELOG.md', 'src/MultiBox.App/MultiBox.App.csproj')

function Get-Prop($Object, [string]$Name) {
    # Optional property read that works under StrictMode.
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $null
}

function Get-ChangeBump {
    <#
    .SYNOPSIS
        Bump for one merged PR (or direct push): a semver:* label wins (largest if several),
        else adding to the announcement file means minor, else patch. Never none.
    #>
    param(
        [string[]]$Labels = @(),
        [string[]]$Files = @()
    )
    $labelBumps = @($Labels | Where-Object { $_ -match '^semver:(major|minor|patch)$' } |
        ForEach-Object { $_ -replace '^semver:', '' })
    if ($labelBumps.Count -gt 0) {
        $best = $labelBumps | Sort-Object { $script:BumpRank[$_] } -Descending | Select-Object -First 1
        return [pscustomobject]@{ Bump = $best; Reason = "label semver:$best" }
    }
    if ($Files -contains $script:AnnouncementFile) {
        return [pscustomobject]@{ Bump = 'minor'; Reason = "adds an announcement ($script:AnnouncementFile)" }
    }
    return [pscustomobject]@{ Bump = 'patch'; Reason = 'default' }
}

function Step-SemVer {
    <# .SYNOPSIS Applies a bump to X.Y.Z. No previous version means the first release, 1.0.0. #>
    param(
        [AllowEmptyString()][string]$Version,
        [Parameter(Mandatory)][ValidateSet('major', 'minor', 'patch')][string]$Bump
    )
    if (-not $Version) { return '1.0.0' }
    if ($Version -notmatch '^v?(\d+)\.(\d+)\.(\d+)$') { throw "Not a SemVer X.Y.Z version: '$Version'" }
    $maj = [int]$Matches[1]; $min = [int]$Matches[2]; $pat = [int]$Matches[3]
    switch ($Bump) {
        'major' { return "$($maj + 1).0.0" }
        'minor' { return "$maj.$($min + 1).0" }
        'patch' { return "$maj.$min.$($pat + 1)" }
    }
}

function Test-BookkeepingCommit {
    <#
    .SYNOPSIS
        True for the commit the release job pushes after tagging (CHANGELOG.md + csproj
        <Version>). It belongs to the release before it, so it must not cause another bump.
    #>
    param(
        [string]$Message,
        [string[]]$Files = @()
    )
    if ($Message -notmatch '^Release v\d+\.\d+\.\d+\b') { return $false }
    if ($Files.Count -eq 0) { return $false }
    foreach ($f in $Files) { if ($script:ChangelogFiles -notcontains $f) { return $false } }
    return $true
}

function Get-NextRelease {
    <#
    .SYNOPSIS
        Next version for a set of unreleased changes. Each change is an object with
        Labels, Files and optionally Number/Title (the PR) and Message (the commit).
        Bookkeeping commits are dropped; if nothing is left there is nothing to release.
    #>
    param(
        [AllowEmptyString()][string]$LatestTag,
        [object[]]$Changes = @()
    )
    $real = @($Changes | Where-Object {
            -not (Test-BookkeepingCommit -Message ([string](Get-Prop $_ 'Message')) -Files @(Get-Prop $_ 'Files'))
        })
    $current = $LatestTag -replace '^v', ''
    if ($real.Count -eq 0) {
        return [pscustomobject]@{
            Release = $false; Bump = 'none'; Version = $current; Tag = $LatestTag
            Reason = 'no unreleased changes'; Prerelease = $false
        }
    }
    $best = 'patch'; $reasons = @()
    foreach ($c in $real) {
        $b = Get-ChangeBump -Labels @(Get-Prop $c 'Labels') -Files @(Get-Prop $c 'Files')
        $number = Get-Prop $c 'Number'
        $who = if ($number) { "#$number" } else { 'direct push' }
        $reasons += "$who=$($b.Bump) ($($b.Reason))"
        if ($script:BumpRank[$b.Bump] -gt $script:BumpRank[$best]) { $best = $b.Bump }
    }
    $prerelease = [bool]($real | Where-Object { @(Get-Prop $_ 'Labels') -contains 'deploy:manual' })
    $version = Step-SemVer -Version $current -Bump $best
    return [pscustomobject]@{
        Release = $true; Bump = $best; Version = $version; Tag = "v$version"
        Reason = ($reasons -join '; '); Prerelease = $prerelease
    }
}

function Test-Announcements {
    <#
    .SYNOPSIS
        Validates whats-new/features.json. Returns a list of error strings (empty = valid).
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Json)
    $errors = [System.Collections.Generic.List[string]]::new()
    try { $doc = $Json | ConvertFrom-Json -ErrorAction Stop }
    catch { $errors.Add("not valid JSON: $($_.Exception.Message)"); return , $errors.ToArray() }
    if ($null -eq $doc -or -not $doc.PSObject.Properties['features'] -or $doc.features -isnot [array]) {
        $errors.Add('top level must be an object with a "features" array'); return , $errors.ToArray()
    }
    $seen = @{}
    $i = 0
    foreach ($f in $doc.features) {
        $at = "features[$i]"
        foreach ($field in 'pr', 'date', 'title', 'description') {
            if (-not $f.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$f.$field)) {
                $errors.Add("${at}: missing $field")
            }
        }
        if ($f.PSObject.Properties['pr']) {
            if ($f.pr -isnot [long] -and $f.pr -isnot [int]) { $errors.Add("${at}: pr must be an integer") }
            elseif ($seen.ContainsKey([long]$f.pr)) { $errors.Add("${at}: pr $($f.pr) is not unique") }
            else { $seen[[long]$f.pr] = $true }
        }
        if ($f.PSObject.Properties['date'] -and [string]$f.date -notmatch '^\d{4}-\d{2}-\d{2}$') {
            $errors.Add("${at}: date must be YYYY-MM-DD")
        }
        if ($f.PSObject.Properties['description'] -and [string]$f.description -match '<[a-zA-Z/]') {
            $errors.Add("${at}: description must be plain text (no HTML)")
        }
        if ($f.PSObject.Properties['images']) {
            $j = 0
            foreach ($img in @($f.images)) {
                if (-not $img.PSObject.Properties['src'] -or -not $img.src) { $errors.Add("${at}.images[$j]: missing src") }
                if (-not $img.PSObject.Properties['caption'] -or -not $img.caption) { $errors.Add("${at}.images[$j]: missing caption") }
                $j++
            }
        }
        $i++
    }
    return , $errors.ToArray()
}

function Get-ChangeLogRows {
    <#
    .SYNOPSIS
        Parses changelog/README.md index rows: | Date | [Name](folder/README.md) | Summary |.
        Returns Date, Name, Folder, Summary for each row.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$IndexContent)
    foreach ($line in $IndexContent -split "`r?`n") {
        if ($line -match '^\|\s*(\d{4}-\d{2}-\d{2})\s*\|\s*\[([^\]]+)\]\(([^)/]+)/README\.md\)\s*\|\s*(.*?)\s*\|\s*$') {
            [pscustomobject]@{ Date = $Matches[1]; Name = $Matches[2]; Folder = $Matches[3]; Summary = $Matches[4] }
        }
    }
}

function New-ReleaseNotes {
    <#
    .SYNOPSIS
        Markdown for one release: the announcement entries, the release-log entries added
        since the previous tag, and every PR that went into it.
    #>
    param(
        [object[]]$Announcements = @(),
        [object[]]$Entries = @(),
        [object[]]$PullRequests = @(),
        [string]$RepoUrl = 'https://github.com/apettey/multi-box'
    )
    $sb = [System.Text.StringBuilder]::new()
    if ($Announcements.Count -gt 0) {
        [void]$sb.AppendLine('### New')
        [void]$sb.AppendLine()
        foreach ($a in $Announcements) {
            $desc = ([string]$a.description -replace "`r?`n`r?`n", ' ') -replace "`r?`n", ' '
            [void]$sb.AppendLine("- **$($a.title).** $desc (#$($a.pr))")
        }
        [void]$sb.AppendLine()
    }
    if ($Entries.Count -gt 0) {
        [void]$sb.AppendLine('### Changes')
        [void]$sb.AppendLine()
        foreach ($e in $Entries) {
            [void]$sb.AppendLine("- [$($e.Name)]($RepoUrl/blob/main/changelog/$($e.Folder)/README.md): $($e.Summary)")
        }
        [void]$sb.AppendLine()
    }
    if ($PullRequests.Count -gt 0) {
        [void]$sb.AppendLine('### Pull requests')
        [void]$sb.AppendLine()
        foreach ($p in $PullRequests) { [void]$sb.AppendLine("- $($p.Title) (#$($p.Number))") }
        [void]$sb.AppendLine()
    }
    if ($sb.Length -eq 0) { [void]$sb.AppendLine('Maintenance release.'); [void]$sb.AppendLine() }
    return $sb.ToString().TrimEnd() + "`n"
}

function Update-Changelog {
    <#
    .SYNOPSIS
        Inserts a "## [X.Y.Z] - date" section (the release notes) above the newest release.
        A leftover hand-written "## [Unreleased]" section is folded into it, so its text
        ships with the first release that follows it. Re-running for a version already in
        the file returns the content unchanged.
    #>
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Date,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Notes
    )
    $nl = if ($Content -match "`r`n") { "`r`n" } else { "`n" }
    if ($Content -match "(?m)^## \[$([regex]::Escape($Version))\]") { return $Content }

    $lines = [System.Collections.Generic.List[string]]($Content -split "`r?`n")
    $unreleasedBody = @()
    $u = $lines.FindIndex({ param($l) $l -match '^## \[Unreleased\]\s*$' })
    if ($u -ge 0) {
        $end = $lines.FindIndex($u + 1, { param($l) $l -match '^## ' })
        if ($end -lt 0) { $end = $lines.Count }
        $unreleasedBody = @($lines.GetRange($u + 1, $end - $u - 1))
        $lines.RemoveRange($u, $end - $u)
    }
    $at = $lines.FindIndex({ param($l) $l -match '^## ' })
    if ($at -lt 0) { $at = $lines.Count }

    $section = [System.Collections.Generic.List[string]]::new()
    $section.Add("## [$Version] - $Date")
    $section.Add('')
    foreach ($l in ($Notes.TrimEnd() -split "`r?`n")) { $section.Add($l) }
    $body = @($unreleasedBody | ForEach-Object { $_ })
    while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0])) { $body = @($body | Select-Object -Skip 1) }
    if ($body.Count -gt 0) {
        $section.Add('')
        $section.Add('### Earlier, unversioned changes')
        $section.Add('')
        foreach ($l in $body) { $section.Add(($l -replace '^### ', '#### ')) }
    }
    while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[$section.Count - 1])) { $section.RemoveAt($section.Count - 1) }
    $section.Add('')
    $lines.InsertRange($at, $section)
    return ($lines -join $nl)
}

function Set-ProjectVersion {
    <# .SYNOPSIS Replaces the <Version>X.Y.Z</Version> value in csproj text. #>
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Version
    )
    if ($Content -notmatch '<Version>[^<]*</Version>') { throw 'No <Version> property in the project file.' }
    return [regex]::Replace($Content, '<Version>[^<]*</Version>', "<Version>$Version</Version>", 1)
}

Export-ModuleMember -Function Get-ChangeBump, Step-SemVer, Test-BookkeepingCommit, Get-NextRelease,
    Test-Announcements, Get-ChangeLogRows, New-ReleaseNotes, Update-Changelog, Set-ProjectVersion
