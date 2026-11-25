#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [switch]$EfsRaw,
    [switch]$ForceRecopy,
    [int]$IpGapMs = 0
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
Assert-Admin
Test-CancelFlag -Config $cfg

$sourceLetter = ([string]$cfg.SourceVolume).TrimEnd('\', ':')
$sourceRoot = $sourceLetter + ':\'
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
$logDir = Join-Path ([string]$cfg.RecoveryRoot) 'logs'
if (-not (Test-Path -LiteralPath $logDir -PathType Container)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
if (-not (Test-Path -LiteralPath $stagingData -PathType Container)) {
    New-Item -ItemType Directory -Path $stagingData -Force | Out-Null
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$roboLog = Join-Path $logDir "robocopy-source-$stamp.log"

$roboArgs = @(
    $sourceRoot,
    $stagingData,
    '/E',
    '/COPYALL',
    '/DCOPY:DAT',
    '/SEC',
    '/B',
    '/XJ',
    '/R:1',
    '/W:1',
    '/NP',
    '/NFL',
    '/NDL',
    "/LOG+:$roboLog"
)
if ($EfsRaw) { $roboArgs += '/EFSRAW' }
if ($ForceRecopy) { $roboArgs += '/IS' }
if ($IpGapMs -gt 0) { $roboArgs += "/IPG:$IpGapMs" }

Write-Host ('Robocopy: {0} -> {1}' -f $sourceRoot, $stagingData)
Write-Host ('Options: IpGapMs={0} EfsRaw={1} ForceRecopy={2}' -f $IpGapMs, [bool]$EfsRaw, [bool]$ForceRecopy)
Write-Host 'Note: /MT is intentionally not combined with /IPG or /EFSRAW.'

& "$env:SystemRoot\System32\robocopy.exe" @roboArgs 2>&1 | ForEach-Object { Write-Host $_ }
$code = $LASTEXITCODE
Write-Host ('Robocopy exit code: {0}' -f $code)
if ($code -ge 8) {
    throw "Robocopy failed with exit code $code. See $roboLog"
}
Write-Host 'Copy completed with an acceptable Robocopy exit code (0-7).'
Write-Host 'Next: run 02-CompareTrees.ps1 -Direction SourceToStaging and require 0 mismatch.'
