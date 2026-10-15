<#
.SYNOPSIS
  Builds the Windows release zips for a version tag: tests, then packages x64 and arm64.
.DESCRIPTION
  Windows counterpart of scripts/release.sh. Writes windows\dist\DiskU-<version>-windows-x64.zip and
  windows\dist\DiskU-<version>-windows-arm64.zip and prints their full paths as the last two output lines.
  Works from any directory and stops on the first failure.
.EXAMPLE
  .\release.ps1 -Tag v1.2.3
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true, Position = 0)][string]$Tag)
$ErrorActionPreference = 'Stop'

if ($Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') {
    [Console]::Error.WriteLine("tag must look like v1.2.3, got $Tag")
    exit 64
}
$version = $Tag.Substring(1)

& (Join-Path $PSScriptRoot 'test.ps1') | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)" }

$dist = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist'))
$zips = foreach ($arch in 'x64', 'arm64') {
    & (Join-Path $PSScriptRoot 'package.ps1') -Version $version -Arch $arch | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Packaging $arch failed (exit $LASTEXITCODE)" }
    $zip = Join-Path $dist "DiskU-$version-windows-$arch.zip"
    if (-not (Test-Path -LiteralPath $zip)) { throw "Packaging $arch produced no $zip" }
    $zip
}
$zips
