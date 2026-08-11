[CmdletBinding()]
param(
    [string] $Version = "0.2.0-local.$(Get-Date -Format 'yyyyMMddHHmmss')"
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseConfigPath = Join-Path $repositoryRoot 'dotnet-releaser.toml'
$previousVersionOverride = $env:MinVerVersionOverride

try {
    $env:MinVerVersionOverride = $Version
    Push-Location $repositoryRoot

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }

    & dotnet tool run dotnet-releaser -- build $releaseConfigPath --force
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-releaser build failed with exit code $LASTEXITCODE."
    }

    & dotnet restore (Join-Path $repositoryRoot 'FindJobHelper.slnx') -p:RestoreLockedMode=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
    $env:MinVerVersionOverride = $previousVersionOverride
}
