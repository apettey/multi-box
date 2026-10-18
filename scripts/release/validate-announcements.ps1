<#
.SYNOPSIS
    Fails if whats-new/features.json is invalid: bad JSON, missing fields, duplicate PRs,
    HTML in descriptions, or images that are not in the repo.
#>
[CmdletBinding()]
param([string]$Path = 'whats-new/features.json')

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force

if (-not (Test-Path $Path)) { Write-Error "$Path not found." }
$json = Get-Content $Path -Raw
$errors = @(Test-Announcements -Json $json)
if ($errors.Count -eq 0) {
    foreach ($f in ($json | ConvertFrom-Json).features) {
        $imgs = if ($f.PSObject.Properties['images']) { @($f.images) } else { @() }
        foreach ($img in $imgs) {
            if (-not (Test-Path $img.src)) { $errors += "pr $($f.pr): image $($img.src) does not exist" }
        }
    }
}
if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "::error file=$Path::$_" }
    exit 1
}
Write-Host "$Path is valid."
