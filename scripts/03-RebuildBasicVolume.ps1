#requires -Version 5.1
<#
  DESTRUCTIVE. Defaults to a dry run.

  Requirements:
    - The latest compare-SourceToStaging-*.json must report Success=true and MismatchCount=0.
    - -Execute, -ConfirmSerial and -IUnderstandThisErasesTheTargetDisk are all required.
    - The target serial is re-read immediately before DiskPart clean.

  This script intentionally does not accept a disk number alone as identity.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [switch]$Execute,
    [string]$ConfirmSerial,
    [switch]$IUnderstandThisErasesTheTargetDisk
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
Assert-Admin
$target = Get-TargetDisk -Config $cfg
$sourceLetter = ([string]$cfg.SourceVolume).TrimEnd('\', ':')
$targetLetter = ([string]$cfg.TargetVolume).TrimEnd('\', ':')
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
$logDir = Join-Path ([string]$cfg.RecoveryRoot) 'logs'

# Require a fresh, successful source-to-staging compare.
$compareFile = Get-ChildItem -LiteralPath $logDir -Filter 'compare-SourceToStaging-*.json' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $compareFile) {
    throw 'No compare-SourceToStaging-*.json found. Run 02-CompareTrees.ps1 first.'
}
$compare = Get-Content -Raw -LiteralPath $compareFile.FullName | ConvertFrom-Json
if (-not $compare.Success -or [int]$compare.MismatchCount -ne 0) {
    throw "Source verification is not clean: Success=$($compare.Success), MismatchCount=$($compare.MismatchCount). Refusing to rebuild."
}
if (-not (Test-Path -LiteralPath $stagingData -PathType Container)) {
    throw "Staging data not found: $stagingData"
}

Write-Host ''
Write-Host '=== REBUILD PLAN ==='
Write-Host ('Target disk: {0}' -f $target.Index)
Write-Host ('Model:       {0}' -f $target.Model)
Write-Host ('Serial:      {0}' -f $target.Serial)
Write-Host ('Firmware:    {0}' -f $target.Firmware)
Write-Host ('Size:        {0}' -f (Format-Bytes $target.Size))
Write-Host ('Source:      {0}:' -f $sourceLetter)
Write-Host ('Staging:     {0}' -f $stagingData)
Write-Host ('Target:      {0}:' -f $targetLetter)
Write-Host ('Verified by: {0}' -f $compareFile.FullName)
Write-Host ''
Write-Host 'This operation will dismount the source volume, clean the target disk,'
Write-Host 'initialize MBR, create a partition and format NTFS.'
Write-Host ''

if (-not $Execute) {
    Write-Host 'DRY RUN: no changes were made.' -ForegroundColor Yellow
    Write-Host 'To execute, add: -Execute -ConfirmSerial <SERIAL> -IUnderstandThisErasesTheTargetDisk'
    return
}
if (-not $IUnderstandThisErasesTheTargetDisk) {
    throw 'Missing -IUnderstandThisErasesTheTargetDisk.'
}
if ([string]::IsNullOrWhiteSpace($ConfirmSerial) -or $ConfirmSerial -ne $target.Serial) {
    throw 'ConfirmSerial must exactly match the target disk serial.'
}
Test-CancelFlag -Config $cfg

$sourceVolume = Get-Volume -DriveLetter $sourceLetter -ErrorAction SilentlyContinue
if (-not $sourceVolume) { throw "Source volume not mounted: $sourceLetter" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$statePath = Join-Path $logDir "rebuild-$stamp.json"
$sourceDestroyed = $false

# 1) Dismount the source volume.
Write-Host ('Dismounting {0}: ...' -f $sourceLetter)
& "$env:SystemRoot\System32\fsutil.exe" volume dismount "$sourceLetter`:" | Out-Null
Start-Sleep -Seconds 2
if (Get-Volume -DriveLetter $sourceLetter -ErrorAction SilentlyContinue) {
    throw 'Source volume is still mounted. Refusing to clean the disk.'
}

# 2) Re-read identity immediately before the destructive command.
$target = Get-TargetDisk -Config $cfg
if ($target.Serial -ne $ConfirmSerial) {
    throw 'Target serial changed before DiskPart clean. Refusing to continue.'
}

# 3) clean via diskpart /s.
Write-Host 'Running DiskPart clean...'
$dp = Invoke-DiskPartFile -Commands @(
    "select disk $($target.Index)",
    'attributes disk clear readonly',
    'clean',
    'exit'
)
$sourceDestroyed = $true
Write-JsonFile -Object ([pscustomobject]@{ Stage = 'Wiped'; At = (Get-Date).ToString('o'); TargetSerial = $target.Serial; DiskPart = $dp }) -Path $statePath

# 4) Wait for the disk to re-enumerate.
$disk = $null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $disk = Get-Disk -Number $target.Index -ErrorAction SilentlyContinue
    if ($disk) { break }
}
if (-not $disk) { throw 'Target disk did not re-enumerate after clean.' }

# 5) Initialize MBR if needed.
if ($disk.PartitionStyle -eq 'RAW') {
    Initialize-Disk -Number $target.Index -PartitionStyle MBR -Confirm:$false -ErrorAction Stop
}
elseif ($disk.PartitionStyle -ne 'MBR') {
    throw "Unexpected partition style after clean: $($disk.PartitionStyle)"
}

# 6) Create a full-size primary partition and assign the target letter.
$partition = Get-Partition -DiskNumber $target.Index -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $partition) {
    $partition = New-Partition -DiskNumber $target.Index -UseMaximumSize -ErrorAction Stop
}
try {
    Add-PartitionAccessPath -DiskNumber $target.Index -PartitionNumber $partition.PartitionNumber -AccessPath "$targetLetter`:\" -ErrorAction Stop
}
catch {
    Write-Host ('Access path may already exist: ' + $_.Exception.Message)
}

# 7) Format NTFS.
$volume = $null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $volume = Get-Volume -DriveLetter $targetLetter -ErrorAction SilentlyContinue
    if ($volume) { break }
}
if (-not $volume) { throw "Target volume $targetLetter`: did not appear." }
if ($volume.FileSystem -ne 'NTFS') {
    Format-Volume -DriveLetter $targetLetter -FileSystem NTFS -NewFileSystemLabel ([string]$cfg.TargetLabel) -AllocationUnitSize 4096 -Confirm:$false -Force -ErrorAction Stop
}
$volume = Get-Volume -DriveLetter $targetLetter -ErrorAction Stop
if ($volume.FileSystem -ne 'NTFS' -or $volume.HealthStatus -ne 'Healthy') {
    throw 'Target volume is not healthy NTFS after format.'
}

$final = [pscustomobject]@{
    CompletedAt = (Get-Date).ToString('o')
    Stage = 'BasicVolumeReady'
    TargetDisk = $target.Index
    TargetSerial = $target.Serial
    PartitionStyle = 'MBR'
    DriveLetter = $targetLetter
    FileSystem = $volume.FileSystem
    VolumeSize = [int64]$volume.Size
    VolumeFree = [int64]$volume.SizeRemaining
    SourceDestroyed = $sourceDestroyed
    Warning = 'Target is empty. Restore from staging before declaring success.'
}
Write-JsonFile -Object $final -Path $statePath
Write-Host ''
Write-Host 'Target is now an empty MBR/NTFS basic volume.' -ForegroundColor Yellow
Write-Host 'Next: run 04-RestoreData.ps1, then 02-CompareTrees.ps1 -Direction StagingToTarget.'
Write-Host ('State: {0}' -f $statePath)
