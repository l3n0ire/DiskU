<#
.SYNOPSIS
  Saves a PNG of a window using PrintWindow with PW_RENDERFULLCONTENT (flag 2).
.DESCRIPTION
  Works even when the window is behind others, unlike CopyFromScreen. Identify the window by its
  title (exact match first, then substring) or by a process name or id.
.EXAMPLE
  .\screenshot.ps1 -Title DiskU -Out $env:TEMP\diskU.png
.EXAMPLE
  .\screenshot.ps1 -Process DiskU -Out shot.png -TimeoutSeconds 30
#>
[CmdletBinding()]
param(
    [string]$Title,
    [string]$Process,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$TimeoutSeconds = 20
)
$ErrorActionPreference = 'Stop'
if (-not $Title -and -not $Process) { throw 'Pass -Title or -Process.' }

if (-not ('DiskUSnap' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DiskUSnap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
}
Add-Type -AssemblyName System.Drawing
[DiskUSnap]::SetProcessDPIAware() | Out-Null

function Find-Window {
    $candidates = Get-Process | Where-Object { $_.MainWindowHandle -ne 0 }
    if ($Process) {
        $id = 0
        if ([int]::TryParse($Process, [ref]$id)) { $candidates = $candidates | Where-Object Id -eq $id }
        else { $candidates = $candidates | Where-Object ProcessName -eq ($Process -replace '\.exe$', '') }
    }
    if ($Title) {
        $exact = $candidates | Where-Object MainWindowTitle -eq $Title
        $candidates = if ($exact) { $exact } else { $candidates | Where-Object { $_.MainWindowTitle -like "*$Title*" } }
    }
    $candidates | Select-Object -First 1
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$target = $null
while ((Get-Date) -lt $deadline) {
    $target = Find-Window
    if ($target) { break }
    Start-Sleep -Milliseconds 300
}
if (-not $target) { throw "No window found (title '$Title', process '$Process') within $TimeoutSeconds s." }

$hwnd = $target.MainWindowHandle
$rect = New-Object DiskUSnap+RECT
[DiskUSnap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.R - $rect.L; $height = $rect.B - $rect.T
if ($width -le 0 -or $height -le 0) { throw "Window '$($target.MainWindowTitle)' has an empty rect (minimized?)." }

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
try { $ok = [DiskUSnap]::PrintWindow($hwnd, $hdc, 2) } finally { $graphics.ReleaseHdc($hdc); $graphics.Dispose() }
if (-not $ok) { $bitmap.Dispose(); throw 'PrintWindow failed.' }

$Out = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $Out))
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "Saved $Out (${width}x$height, '$($target.MainWindowTitle)', pid $($target.Id))"
