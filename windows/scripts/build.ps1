<#
.SYNOPSIS
  Builds windows/DiskU.sln (Core, Tests and the WinUI app).
.EXAMPLE
  .\build.ps1                 # Release
  .\build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot '..\DiskU.sln'
& dotnet build $solution -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$exe = Join-Path $PSScriptRoot "..\src\DiskU.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64\DiskU.exe"
Write-Host "App: $([IO.Path]::GetFullPath($exe))"
