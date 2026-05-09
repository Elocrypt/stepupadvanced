#requires -Version 5.1

<#
.SYNOPSIS
    Build and package StepUp Advanced as a release zip.

.DESCRIPTION
    Builds the main project in the requested configuration, then zips the
    contents of src/StepUpAdvanced/bin/<Configuration>/Mods/stepupadvanced/
    as build/dist/StepUpAdvanced_<Version>.zip — ready to drop into a Vintage
    Story Mods/ folder or upload to the mod portal.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Version
    Version string for the output filename. Required. Should match the version
    field in modinfo.json.

.EXAMPLE
    ./build/package.ps1 -Version 1.2.5
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

# Resolve repo root from the script's own location so this works regardless
# of the caller's CWD.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$srcProject = Join-Path $repoRoot 'src/StepUpAdvanced/StepUpAdvanced.csproj'
$buildOutput = Join-Path $repoRoot "src/StepUpAdvanced/bin/$Configuration/Mods/StepUpAdvanced"
$distDir = Join-Path $repoRoot 'build/dist'
$zipPath = Join-Path $distDir "StepUpAdvanced_$Version.zip"

Write-Host "Building $srcProject ($Configuration)..." -ForegroundColor Cyan
& dotnet build $srcProject -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $buildOutput)) {
    throw "Expected build output not found at $buildOutput"
}

# Verify the modinfo.json version matches the requested version, so we don't
# accidentally ship StepUpAdvanced_1.2.5.zip with modinfo saying 1.2.4.
$modinfoPath = Join-Path $buildOutput 'modinfo.json'
if (Test-Path $modinfoPath) {
    $modinfo = Get-Content $modinfoPath -Raw | ConvertFrom-Json
    if ($modinfo.version -ne $Version) {
        Write-Warning "modinfo.json version is '$($modinfo.version)' but packaging as '$Version'. Update modinfo.json before tagging."
    }
}

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath
}

Write-Host "Packaging $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$buildOutput/*" -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Done: $zipPath" -ForegroundColor Green
