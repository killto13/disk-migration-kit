#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'Common.ps1')

$cfg = Import-MigrationConfig -Path $Config
$target = Get-TargetDisk -Config $cfg
$targetLetter = ([string]$cfg.TargetVolume).TrimEnd('\', ':')
$stagingData = Join-Path ([string]$cfg.StagingRoot) 'DATA'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path ([string]$cfg.RecoveryRoot) 'postcheck' }
if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonPath = Join-Path $OutputDir "POST_CHECK_$stamp.json"
$mdPath = Join-Path $OutputDir "POST_CHECK_$stamp.md"

$disk = Get-Disk -Number $target.Index -ErrorAction SilentlyContinue
$partition = Get-Partition -DriveLetter $targetLetter -ErrorAction SilentlyContinue
$volume = Get-Volume -DriveLetter $targetLetter -ErrorAction SilentlyContinue
$problems = New-Object System.Collections.Generic.List[string]

if (-not $disk) { $problems.Add('Target disk is not enumerated by Get-Disk.') }
elseif ($disk.PartitionStyle -ne 'MBR') { $problems.Add("PartitionStyle is $($disk.PartitionStyle), expected MBR.") }
if (-not $partition) { $problems.Add("No partition found for $targetLetter`:") }
if (-not $volume) { $problems.Add("Volume $targetLetter`: not found.") }
elseif ($volume.FileSystem -ne 'NTFS' -or $volume.HealthStatus -ne 'Healthy') { $problems.Add('Target volume is not healthy NTFS.') }

function Get-Stats {
    param([string]$Root)
    $files = 0; $dirs = 0; [int64]$bytes = 0
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return [pscustomobject]@{ Files = 0; Directories = 0; Bytes = 0 } }
    Get-ChildItem -LiteralPath $Root -Force -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        $rel = $_.FullName.Substring($Root.Length).TrimStart('\')
        if ($rel -match '(?i)^(System Volume Information|\$RECYCLE\.BIN)(\\|$)') { return }
        if ($_.PSIsContainer) { $dirs++ } else { $files++; $bytes += [int64]$_.Length }
    }
    return [pscustomobject]@{ Files = $files; Directories = $dirs; Bytes = $bytes }
}

$targetStats = Get-Stats -Root ($targetLetter + ':\')
$stagingStats = Get-Stats -Root $stagingData
if ($targetStats.Files -ne $stagingStats.Files) { $problems.Add("File count mismatch: target=$($targetStats.Files), staging=$($stagingStats.Files)") }
if ($targetStats.Directories -ne $stagingStats.Directories) { $problems.Add("Directory count mismatch: target=$($targetStats.Directories), staging=$($stagingStats.Directories)") }
if ($targetStats.Bytes -ne $stagingStats.Bytes) { $problems.Add("Byte count mismatch: target=$($targetStats.Bytes), staging=$($stagingStats.Bytes)") }

$chkText = $null
$chkCode = $null
if ($isAdmin) {
    $chkText = & "$env:SystemRoot\System32\chkdsk.exe" "$targetLetter`:" /scan 2>&1 | Out-String
    $chkCode = $LASTEXITCODE
    if ($chkCode -ne 0) { $problems.Add("chkdsk /scan exit code $chkCode") }
    if ($chkText -match '(?i)(found problems|发现问题|坏扇区.*[1-9])') { $problems.Add('chkdsk reported potential problems.') }
}
else {
    $problems.Add('Not elevated: chkdsk/SMART checks were skipped.')
}

$smart = $null
if ($isAdmin) {
    try {
        $smart = Get-PhysicalDisk | Where-Object SerialNumber -eq $target.Serial |
            Get-StorageReliabilityCounter | Select-Object ReadErrorsTotal,ReadErrorsCorrected,ReadErrorsUncorrected,Temperature,PowerOnHours
        if ($smart -and $smart.ReadErrorsUncorrected -gt 0) { $problems.Add('SMART reports uncorrected read errors.') }
    }
    catch { $smart = $null }
}

$result = [pscustomobject]@{
    CheckedAt = (Get-Date).ToString('o')
    IsAdmin = $isAdmin
    TargetDisk = [pscustomobject]@{ Number = $target.Index; Model = $target.Model; Serial = $target.Serial; Firmware = $target.Firmware; PartitionStyle = if ($disk) { [string]$disk.PartitionStyle } else { $null } }
    Partition = if ($partition) { [pscustomobject]@{ Number = $partition.PartitionNumber; DriveLetter = $targetLetter; Type = [string]$partition.Type; Offset = [int64]$partition.Offset; Size = [int64]$partition.Size } } else { $null }
    Volume = if ($volume) { [pscustomobject]@{ FileSystem = $volume.FileSystem; Health = $volume.HealthStatus; Size = [int64]$volume.Size; Free = [int64]$volume.SizeRemaining; UniqueId = [string]$volume.UniqueId } } else { $null }
    TargetStats = $targetStats
    StagingStats = $stagingStats
    ChkdskExitCode = $chkCode
    Smart = $smart
    Problems = @($problems)
    Success = ($problems.Count -eq 0)
}
Write-JsonFile -Object $result -Path $jsonPath

$md = @(
    '# Post-check',
    '',
    ('- Time: {0}' -f $result.CheckedAt),
    ('- Success: {0}' -f $result.Success),
    ('- PartitionStyle: {0}' -f $(if ($disk) { $disk.PartitionStyle } else { 'missing' })),
    ('- Volume: {0}' -f $(if ($volume) { $volume.FileSystem + '/' + $volume.HealthStatus } else { 'missing' })),
    ('- Files: target={0}, staging={1}' -f $targetStats.Files, $stagingStats.Files),
    ('- Directories: target={0}, staging={1}' -f $targetStats.Directories, $stagingStats.Directories),
    ('- Bytes: target={0}, staging={1}' -f $targetStats.Bytes, $stagingStats.Bytes),
    '',
    '## Problems',
    ''
)
if ($problems.Count -eq 0) { $md += '- None.' } else { foreach ($p in $problems) { $md += ('- ' + $p) } }
$md | Set-Content -LiteralPath $mdPath -Encoding UTF8

Write-Host ''
Write-Host ('Success: {0}' -f $result.Success)
Write-Host ('Files: target={0}, staging={1}' -f $targetStats.Files, $stagingStats.Files)
Write-Host ('Dirs:  target={0}, staging={1}' -f $targetStats.Directories, $stagingStats.Directories)
Write-Host ('Bytes: target={0}, staging={1}' -f $targetStats.Bytes, $stagingStats.Bytes)
if ($problems.Count -gt 0) {
    Write-Host 'Problems:' -ForegroundColor Red
    $problems | ForEach-Object { Write-Host ('  - ' + $_) }
    Write-Host ('Report: ' + $mdPath)
    exit 1
}
Write-Host 'Post-check passed.' -ForegroundColor Green
Write-Host ('Report: ' + $mdPath)
