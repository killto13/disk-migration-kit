#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [Parameter(Mandatory = $true)]
    [ValidateSet('SourceToStaging','StagingToTarget')]
    [string]$Direction,
    [string]$Name = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
$sourceLetter = ([string]$cfg.SourceVolume).TrimEnd('\', ':')
$sourceRoot = $sourceLetter + ':\'
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
$targetRoot = ([string]$cfg.TargetVolume).TrimEnd('\', ':') + ':\'
if ([string]::IsNullOrWhiteSpace($Name)) { $Name = $Direction }

if ($Direction -eq 'SourceToStaging') {
    $compareSource = $sourceRoot
    $compareDestination = $stagingData
}
else {
    $compareSource = $stagingData
    $compareDestination = $targetRoot
}
if (-not (Test-Path -LiteralPath $compareSource -PathType Container)) { throw "Source not found: $compareSource" }
if (-not (Test-Path -LiteralPath $compareDestination -PathType Container)) { throw "Destination not found: $compareDestination" }

$logDir = Join-Path ([string]$cfg.RecoveryRoot) 'logs'
if (-not (Test-Path -LiteralPath $logDir -PathType Container)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$progressPath = Join-Path $logDir ("$Name-progress-$stamp.csv")
$resultPath = Join-Path $logDir ("compare-$Name-$stamp.json")

Add-Type -Path (Join-Path $PSScriptRoot 'TreeCompare.cs') -ErrorAction Stop
Write-Host ('Starting byte-for-byte comparison: {0} -> {1}' -f $compareSource, $compareDestination)
Write-Host ('Progress: {0}' -f $progressPath)

$result = [TreeCompare]::Compare($compareSource, $compareDestination, 'System Volume Information', $progressPath)
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8

Write-Host ''
Write-Host ('Success: {0}' -f $result.Success)
Write-Host ('SourceFiles: {0}; DestinationFiles: {1}' -f $result.SourceFiles, $result.DestinationFiles)
Write-Host ('SourceDirectories: {0}; DestinationDirectories: {1}' -f $result.SourceDirectories, $result.DestinationDirectories)
Write-Host ('SourceBytes: {0}; DestinationBytes: {1}' -f $result.SourceBytes, $result.DestinationBytes)
Write-Host ('MismatchCount: {0}' -f $result.MismatchCount)
Write-Host ('Result: {0}' -f $resultPath)
if (-not $result.Success) {
    Write-Host ''
    Write-Host 'MISMATCHES:' -ForegroundColor Red
    $result.Mismatches | Select-Object -First 50 | ForEach-Object { Write-Host ('  ' + $_) }
    exit 1
}
Write-Host '0 mismatch. This direction is verified.' -ForegroundColor Green
