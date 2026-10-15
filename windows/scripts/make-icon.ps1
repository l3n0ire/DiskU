<#
.SYNOPSIS
  Makes windows/src/DiskU.App/Assets/AppIcon.ico from Resources/AppIcon.png.
.DESCRIPTION
  Writes one PNG-compressed image per size (16, 24, 32, 48, 64, 128, 256 px) into a single .ico.
  Also writes Assets\AppIcon.png (64 px) for the title bar icon.
  Never edit either file by hand; rerun this script when Resources/AppIcon.png changes.
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Source) { $Source = Join-Path $here '..\..\Resources\AppIcon.png' }
if (-not $Output) { $Output = Join-Path $here '..\src\DiskU.App\Assets\AppIcon.ico' }
Add-Type -AssemblyName System.Drawing

$Source = (Resolve-Path $Source).Path
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null

$sizes = 16, 24, 32, 48, 64, 128, 256
$original = [System.Drawing.Image]::FromFile($Source)
$images = @()
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bitmap)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($original, 0, 0, $size, $size)
        $g.Dispose()
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        $images += , @{ Size = $size; Bytes = $stream.ToArray() }
        if ($size -eq 64) { [IO.File]::WriteAllBytes([IO.Path]::ChangeExtension($Output, ".png"), $stream.ToArray()) }
        $stream.Dispose()
    }
} finally { $original.Dispose() }

# ICONDIR, then one ICONDIRENTRY per image, then the PNG data.
$file = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$image.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $w.Write([byte[]]$image.Bytes) }
$w.Flush()
[IO.File]::WriteAllBytes($Output, $file.ToArray())
$w.Dispose()
Write-Host "Wrote $Output ($($sizes -join ", ") px) and $([IO.Path]::ChangeExtension($Output, ".png")) (64 px)"
