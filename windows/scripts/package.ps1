<#
.SYNOPSIS
  Publishes a self-contained DiskU and zips it to windows/dist/DiskU-<version>-windows-<arch>.zip.
.EXAMPLE
  .\package.ps1                         # version from Directory.Build.props, x64
  .\package.ps1 -Version 1.0.5 -Arch arm64
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64'
)
$ErrorActionPreference = 'Stop'
$windows = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $windows 'Directory.Build.props'))
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
}
$Version = $Version.TrimStart('v')
$platform = if ($Arch -eq 'arm64') { 'ARM64' } else { 'x64' }
$project = Join-Path $windows 'src\DiskU.App\DiskU.App.csproj'
$dist = Join-Path $windows 'dist'
$publish = Join-Path $dist "DiskU-$Version-windows-$Arch"
$zip = "$publish.zip"

if (Test-Path $publish) { [IO.Directory]::Delete($publish, $true) }
& dotnet publish $project -c Release -r "win-$Arch" -p:Platform=$platform -p:Version=$Version --self-contained -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path (Join-Path $publish 'DiskU.exe'))) { throw "Publish produced no DiskU.exe in $publish" }

if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host "Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
