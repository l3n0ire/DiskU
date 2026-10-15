<#
.SYNOPSIS
  Runs the DiskU.Core tests (MSTest 4). Extra arguments go to dotnet test, e.g. --filter Name~Scanner.
.NOTES
  Run unelevated: UnreadableDirectoryCountsAsInaccessible is inconclusive as administrator.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\DiskU.Core.Tests'
& dotnet test $project -c $Configuration @Rest
exit $LASTEXITCODE
