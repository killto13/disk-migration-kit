#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [switch]$EfsRaw
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
Assert-Admin
$target = Get-TargetDisk -Config $cfg
$targetLetter = ([string]$cfg.TargetVolume).TrimEnd('\', ':')
$targetRoot = $targetLetter + ':\'
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
if (-not (Test-Path -LiteralPath $stagingData -PathType Container)) { throw "Staging data not found: $stagingData" }

$volume = Get-Volume -DriveLetter $targetLetter -ErrorAction SilentlyContinue
if (-not $volume) { throw "Target volume not mounted: $targetLetter" }
if ($volume.FileSystem -ne 'NTFS' -or $volume.HealthStatus -ne 'Healthy') {
    throw "Target volume is not healthy NTFS: $($volume.FileSystem)/$($volume.HealthStatus)"
}

$logDir = Join-Path ([string]$cfg.RecoveryRoot) 'logs'
if (-not (Test-Path -LiteralPath $logDir -PathType Container)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$roboLog = Join-Path $logDir "robocopy-restore-$stamp.log"

$roboArgs = @(
    $stagingData,
    $targetRoot,
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

Write-Host ('Robocopy restore: {0} -> {1}' -f $stagingData, $targetRoot)
& "$env:SystemRoot\System32\robocopy.exe" @roboArgs 2>&1 | ForEach-Object { Write-Host $_ }
$code = $LASTEXITCODE
Write-Host ('Robocopy exit code: {0}' -f $code)
if ($code -ge 8) {
    throw "Robocopy restore failed with exit code $code. See $roboLog"
}
Write-Host 'Restore completed with an acceptable Robocopy exit code (0-7).'
Write-Host 'Next: run 02-CompareTrees.ps1 -Direction StagingToTarget.'
