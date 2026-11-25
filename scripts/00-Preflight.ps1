#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
$target = Get-TargetDisk -Config $cfg
$sourceLetter = ([string]$cfg.SourceVolume).TrimEnd('\', ':')
$sourceRoot = $sourceLetter + ':\'
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path ([string]$cfg.RecoveryRoot) 'manifests'
}
if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultPath = Join-Path $OutputDir "preflight-$stamp.json"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$sourceVolume = Get-Volume -DriveLetter $sourceLetter -ErrorAction SilentlyContinue
if (-not $sourceVolume) { throw "Source volume not found: $sourceLetter" }
if ($sourceVolume.FileSystem -ne 'NTFS' -or $sourceVolume.HealthStatus -ne 'Healthy') {
    throw "Source volume is not healthy NTFS: $($sourceVolume.FileSystem)/$($sourceVolume.HealthStatus)"
}
$sourceLogical = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$sourceLetter`:'" -ErrorAction SilentlyContinue

$stagingLetter = ([string]$cfg.StagingRoot).Substring(0, 1)
$stagingVolume = Get-Volume -DriveLetter $stagingLetter -ErrorAction SilentlyContinue
if (-not $stagingVolume) { throw "Staging volume not found: $stagingLetter" }
if ($stagingVolume.HealthStatus -ne 'Healthy') { throw "Staging volume is not healthy: $stagingLetter" }

$sourceBytes = [int64]$sourceVolume.Size - [int64]$sourceVolume.SizeRemaining
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
$existingStagingBytes = [int64]0
if (Test-Path -LiteralPath $stagingData -PathType Container) {
    $existingStagingBytes = [int64](Get-ChildItem -LiteralPath $stagingData -Force -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}
if ($existingStagingBytes -gt 0) {
    # Reusing an existing staging copy: only the missing bytes plus a safety margin.
    $requiredBytes = [int64][math]::Max([double]5GB, [double]([int64]$sourceBytes - [int64]$existingStagingBytes) + [double]5GB)
}
else {
    # Fresh copy: keep a 10% margin.
    $requiredBytes = [int64][math]::Max([double]$sourceBytes * 1.10, [double]5GB)
}
$freeBytes = [int64]$stagingVolume.SizeRemaining
if ($freeBytes -lt $requiredBytes) {
    throw "Insufficient staging space. Required=$requiredBytes, free=$freeBytes"
}

$diskInfo = $null
try { $diskInfo = Get-Disk -Number ([int]$cfg.TargetDiskNumber) -ErrorAction Stop } catch {}
$partitionInfo = @(Get-CimInstance Win32_DiskPartition -ErrorAction SilentlyContinue |
    Where-Object DiskIndex -eq ([int]$cfg.TargetDiskNumber) |
    Select-Object DiskIndex,Index,Type,StartingOffset,Size,NumberOfBlocks,PrimaryPartition,Bootable,BootPartition)

$result = [pscustomobject]@{
    CapturedAt = (Get-Date).ToString('o')
    IsAdmin = $isAdmin
    Target = [pscustomobject]@{
        DiskNumber = $target.Index
        Model = $target.Model
        Serial = $target.Serial
        Firmware = $target.Firmware
        Size = $target.Size
        PartitionStyle = if ($diskInfo) { [string]$diskInfo.PartitionStyle } else { $null }
        OperationalStatus = if ($diskInfo) { [string]$diskInfo.OperationalStatus } else { $null }
        HealthStatus = if ($diskInfo) { [string]$diskInfo.HealthStatus } else { $null }
        IsBoot = if ($diskInfo) { [bool]$diskInfo.IsBoot } else { $null }
        IsSystem = if ($diskInfo) { [bool]$diskInfo.IsSystem } else { $null }
        Partitions = @($partitionInfo)
    }
    Source = [pscustomobject]@{
        Volume = $sourceLetter
        FileSystem = $sourceVolume.FileSystem
        Health = $sourceVolume.HealthStatus
        Size = [int64]$sourceVolume.Size
        Free = [int64]$sourceVolume.SizeRemaining
        Used = $sourceBytes
        NtfsSerial32 = if ($sourceLogical) { [string]$sourceLogical.VolumeSerialNumber } else { $null }
        UniqueId = [string]$sourceVolume.UniqueId
    }
    Staging = [pscustomobject]@{
        Root = [string]$cfg.StagingRoot
        Data = $stagingData
        ExistingBytes = $existingStagingBytes
        Free = $freeBytes
        Required = $requiredBytes
    }
    Checks = [pscustomobject]@{
        TargetIdentity = $true
        TargetNotBoot = @($partitionInfo | Where-Object { $_.Bootable -or $_.BootPartition }).Count -eq 0
        SourceHealthy = $true
        StagingSpace = $true
    }
    Warning = 'Extent order and LDM layout must be verified separately with DiskPart detail/list volume. A single contiguous MBR partition is not equivalent to a multi-extent dynamic volume.'
}

Write-JsonFile -Object $result -Path $resultPath
Write-Host ''
Write-Host '=== PREFLIGHT PASS ==='
Write-Host ('Target: disk {0}, {1}, serial {2}, firmware {3}, {4}' -f $target.Index, $target.Model, $target.Serial, $target.Firmware, (Format-Bytes $target.Size))
Write-Host ('Source: {0}: {1}, {2}, used {3}' -f $sourceLetter, $sourceVolume.FileSystem, $sourceVolume.HealthStatus, (Format-Bytes $sourceBytes))
Write-Host ('Staging existing: {0}; free: {1}; required: {2}' -f (Format-Bytes $existingStagingBytes), (Format-Bytes $freeBytes), (Format-Bytes $requiredBytes))
Write-Host ('Report: {0}' -f $resultPath)
Write-Host ''
Write-Host 'WARNING: verify the dynamic volume extent order before any rebuild.'
Write-Host '         This preflight does not authorize a destructive operation.'
