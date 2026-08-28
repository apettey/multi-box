<#
.SYNOPSIS
    Builds MultiBox for Windows.

.EXAMPLE
    .\build.ps1                 # framework-dependent build (needs .NET 10 Desktop Runtime)
    .\build.ps1 -SelfContained  # standalone build, no runtime install needed (~145 MB)
    .\build.ps1 -Test           # run the test suite first
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if ($Test) {
    Write-Host "Running tests..." -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests/MultiBox.Core.Tests/MultiBox.Core.Tests.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

$outDir = if ($SelfContained) { 'dist/win-x64-standalone' } else { 'dist/win-x64' }
$sc = if ($SelfContained) { 'true' } else { 'false' }

Write-Host "Publishing to $outDir (self-contained: $sc)..." -ForegroundColor Cyan

dotnet publish (Join-Path $root 'src/MultiBox.App/MultiBox.App.csproj') `
    -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true `
    -o (Join-Path $root $outDir) --nologo

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# The console diagnostic tool ships alongside the GUI.
dotnet publish (Join-Path $root 'src/MultiBox.Replay/MultiBox.Replay.csproj') `
    -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true `
    -o (Join-Path $root $outDir) --nologo

Write-Host "`nDone. Run $outDir\MultiBox.exe" -ForegroundColor Green
Write-Host "Check your setup first with: $outDir\multibox-replay.exe doctor" -ForegroundColor Green
