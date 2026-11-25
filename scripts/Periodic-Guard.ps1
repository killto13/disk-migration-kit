#requires -Version 5.1
<#
  Read-only periodic guard for a running migration.

  It checks:
    - wrapper process alive (optional -WrapperProcessId)
    - target disk identity still matches
    - target volume state
    - staging DATA presence and free space
    - latest compare progress CSV and mismatch count
    - SUCCESS/FAILED markers for the current RunStamp

  It never writes to the target or staging disks. It only appends a local log.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [Parameter(Mandatory = $true)][string]$RunStamp,
    [int]$WrapperProcessId = 0,
    [int]$IntervalSeconds = 300,
    [string]$OutputDir = '',
    [switch]$Once
)
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
$target = Get-TargetDisk -Config $cfg
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
$logDir = Join-Path ([string]$cfg.RecoveryRoot) 'logs'
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path ([string]$cfg.RecoveryRoot) 'periodic_check' }
if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logPath = Join-Path $OutputDir "periodic-guard-$stamp.log"

function Write-Guard {
    param([string]$Message)
    $line = '[{0}] {1}' -f (Get-Date).ToString('o'), $Message
    [IO.File]::AppendAllText($logPath, $line + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Host $line
}

function Get-LatestProgress {
    param([string]$Root, [string]$Stamp)
    $file = Get-ChildItem -LiteralPath (Join-Path $Root 'logs') -Filter ('*progress-' + $Stamp + '.csv') -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $file) { return $null }
    $line = Get-Content -LiteralPath $file.FullName -Tail 1 -ErrorAction SilentlyContinue
    if ($line -match '^(\d{4}-\d{2}-\d{2}T[^,]+),(\d+),(\d+),(\d+)\s*$') {
        return [pscustomobject]@{
            File = $file.FullName
            Time = [datetimeoffset]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture).LocalDateTime
            Bytes = [int64]$Matches[2]
            Files = [int64]$Matches[3]
            Mismatches = [int]$Matches[4]
        }
    }
    return [pscustomobject]@{ File = $file.FullName; Time = $file.LastWriteTime; Bytes = 0; Files = 0; Mismatches = 0 }
}

$checkNo = 0
while ($true) {
    $checkNo++
    $now = Get-Date
    $problems = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]

    $successPath = Join-Path ([string]$cfg.RecoveryRoot) 'SUCCESS.json'
    $failedPath = Join-Path ([string]$cfg.RecoveryRoot) ("FAILED-$RunStamp.json")
    $success = Test-Path -LiteralPath $successPath -PathType Leaf
    $failed = Test-Path -LiteralPath $failedPath -PathType Leaf

    $wrapperAlive = $true
    if ($WrapperProcessId -gt 0) {
        $wrapperAlive = [bool](Get-Process -Id $WrapperProcessId -ErrorAction SilentlyContinue)
    }

    if (-not $wrapperAlive -and -not $success -and -not $failed) {
        $problems.Add('Wrapper process is not running and no SUCCESS/FAILED marker exists.')
    }

    $current = Get-TargetDisk -Config $cfg
    if ($current.Serial -ne $target.Serial) { $problems.Add('Target serial changed.') }

    $volume = Get-Volume -DriveLetter (([string]$cfg.TargetVolume).TrimEnd('\', ':')) -ErrorAction SilentlyContinue
    if ($volume) {
        if ($volume.FileSystem -ne 'NTFS' -or $volume.HealthStatus -ne 'Healthy') {
            $problems.Add(('Target volume state: {0}/{1}' -f $volume.FileSystem, $volume.HealthStatus))
        }
    }
    elseif (-not $success) {
        $warnings.Add('Target volume is not mounted (may be normal during rebuild/restore).')
    }

    if (-not (Test-Path -LiteralPath $stagingData -PathType Container)) { $problems.Add('Staging DATA directory missing.') }
    $stagingVolume = Get-Volume -DriveLetter (([string]$cfg.StagingRoot).Substring(0, 1)) -ErrorAction SilentlyContinue
    if ($stagingVolume -and $stagingVolume.SizeRemaining -lt 5GB) { $problems.Add('Staging free space is below 5 GB.') }

    $progress = Get-LatestProgress -Root ([string]$cfg.RecoveryRoot) -Stamp $RunStamp
    if ($progress) {
        if ($progress.Mismatches -gt 0) { $problems.Add(('Mismatches detected: {0}' -f $progress.Mismatches)) }
        if (($now - $progress.Time).TotalMinutes -gt 15) { $warnings.Add('Progress CSV has not changed for more than 15 minutes.') }
    }

    $status = if ($problems.Count -gt 0) { 'FAIL' } elseif ($warnings.Count -gt 0) { 'WARN' } else { 'PASS' }
    $summary = [pscustomobject]@{
        CheckNo = $checkNo
        CheckedAt = $now.ToString('o')
        Status = $status
        RunStamp = $RunStamp
        WrapperAlive = $wrapperAlive
        TargetSerial = $current.Serial
        TargetVolume = if ($volume) { [pscustomobject]@{ FileSystem = $volume.FileSystem; Health = $volume.HealthStatus; Free = [int64]$volume.SizeRemaining } } else { $null }
        Progress = if ($progress) { [pscustomobject]@{ File = $progress.File; Time = $progress.Time.ToString('o'); Files = $progress.Files; Bytes = $progress.Bytes; Mismatches = $progress.Mismatches } } else { $null }
        Warnings = @($warnings)
        Problems = @($problems)
        Success = $success
        Failed = $failed
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDir 'latest.json') -Encoding UTF8
    Write-Guard ('CHECK {0} {1} | Serial={2} | Volume={3} | Progress={4} | Mismatch={5}' -f `
        $checkNo, $status, $current.Serial,
        $(if ($volume) { $volume.FileSystem + '/' + $volume.HealthStatus } else { 'missing' }),
        $(if ($progress) { ('{0:N0} files / {1}' -f $progress.Files, (Format-Bytes $progress.Bytes)) } else { 'n/a' }),
        $(if ($progress) { $progress.Mismatches } else { 'n/a' }))
    foreach ($w in $warnings) { Write-Guard ('  WARN: ' + $w) }
    foreach ($p in $problems) { Write-Guard ('  FAIL: ' + $p) }

    if ($success) {
        Write-Guard 'FINAL: SUCCESS marker found.'
        $disk = Get-Disk -Number $current.Index -ErrorAction SilentlyContinue
        Write-Guard ('FINAL: PartitionStyle={0}; Volume={1}' -f $(if ($disk) { $disk.PartitionStyle } else { 'missing' }), $(if ($volume) { $volume.FileSystem } else { 'missing' }))
        exit 0
    }
    if ($failed) {
        Write-Guard 'FINAL: FAILED marker found.'
        exit 1
    }
    if ($Once) { exit 0 }
    Start-Sleep -Seconds ([math]::Max(30, $IntervalSeconds))
}
