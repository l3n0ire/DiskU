<#
.SYNOPSIS
  Port of scripts/make-fixture.sh: a disk-usage fixture with known sizes (whole 4 KiB blocks of random data).
.DESCRIPTION
  Expected allocated total: 106,958,848 bytes (104,452 KiB) in 17 files plus 2 links. DiskU shows "107 MB".
  Differences from the Mac script:
  - Apps\Tool.app is a plain folder (Windows has no app bundles).
  - Code\app\loop is a directory symlink to ..\.. when this account may create symlinks (Developer Mode or
    administrator), otherwise a junction to the fixture root. Docs\trip-alias.mov is a file symlink, otherwise
    a junction to Movies. Either way it is a reparse point, which the scanner never follows, so totals match.
  - Like the Mac script it replaces <Root>, but it refuses to remove a non-empty folder that is not an
    earlier fixture, and it removes with rmdir, which never follows links.
.EXAMPLE
  .\make-fixture.ps1 $env:TEMP\disku-fixture
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true, Position = 0)][string]$Root)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $Root))

if (Test-Path -LiteralPath $Root) {
    $isFixture = Test-Path -LiteralPath (Join-Path $Root 'Movies\trip.mov')
    $isEmpty = -not (Get-ChildItem -LiteralPath $Root -Force | Select-Object -First 1)
    if (-not ($isFixture -or $isEmpty)) { throw "$Root exists and is not a DiskU fixture; refusing to replace it." }
    & cmd.exe /c "rmdir /s /q `"$Root`""
    if (Test-Path -LiteralPath $Root) { throw "Could not remove $Root" }
}

$folders = 'Movies', 'Pictures\Vacation', 'Pictures\Screens', 'Music', 'Code\app\src', 'Docs', 'Archives', 'Apps\Tool.app\Contents\MacOS'
foreach ($folder in $folders) { New-Item -ItemType Directory -Force (Join-Path $Root $folder) | Out-Null }

$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
function mk([string]$relative, [int]$kib) {
    $bytes = New-Object byte[] ($kib * 1024)
    $rng.GetBytes($bytes)
    [IO.File]::WriteAllBytes((Join-Path $Root $relative), $bytes)
}

mk 'Movies\trip.mov'              51200   # 50 MiB
mk 'Pictures\Vacation\beach.heic'  6144
mk 'Pictures\Vacation\sunset.jpg'  4096
mk 'Pictures\Vacation\dunes.jpg'   2048
mk 'Pictures\Screens\shot1.png'    4096
mk 'Pictures\Screens\shot2.png'    4096   # images total 20 MiB
mk 'Music\song.m4a'                8192
mk 'Music\demo.wav'                4096
mk 'Code\app\src\main.swift'         64
mk 'Code\app\src\data.json'        1984
mk 'Code\app\build.o'              2048
mk 'Docs\report.pdf'               3072
mk 'Docs\notes.txt'                   4
mk 'Docs\slides.key'               2044
mk 'Archives\backup.zip'           6144
mk 'Apps\Tool.app\Contents\MacOS\Tool' 5120
mk 'Apps\Tool.app\Contents\Info.plist'    4
$rng.Dispose()

function link([string]$relative, [string]$target, [string]$junctionTarget, [switch]$Directory) {
    $path = Join-Path $Root $relative
    $flag = if ($Directory) { '/D ' } else { '' }
    & cmd.exe /c "mklink $flag`"$path`" `"$target`"" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & cmd.exe /c "mklink /J `"$path`" `"$junctionTarget`"" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not create $relative" }
        Write-Host "  $relative is a junction (no symlink privilege)"
    }
}
link 'Code\app\loop' '..\..' $Root -Directory
link 'Docs\trip-alias.mov' '..\Movies\trip.mov' (Join-Path $Root 'Movies')

Write-Host "Fixture at $Root (expect 106,958,848 bytes = 107 MB, 17 files)"
