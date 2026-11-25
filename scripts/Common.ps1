#requires -Version 5.1
<#
Common helpers for the E: Disk Migration Kit.

Safety rules:
  - No destructive command is executed by these helpers.
  - DiskPart is invoked with /s, not by writing to stdin.
  - Callers must do identity checks before calling destructive operations.
#>

$ErrorActionPreference = 'Stop'
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This operation must run as Administrator or SYSTEM.'
    }
}

function Import-MigrationConfig {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Config file not found: $Path"
    }
    $config = Import-PowerShellDataFile -LiteralPath $Path
    foreach ($key in @('TargetDiskNumber','TargetModel','TargetSerial','TargetFirmware','SourceVolume','StagingRoot','RecoveryRoot','TargetVolume')) {
        if (-not $config.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$config[$key])) {
            throw "Config is missing required key: $key"
        }
    }
    # Staging and recovery may share the same root, but recovery must not be
    # inside the staging DATA tree (logs would contaminate the verified data).
    try {
        $stagingFull = [IO.Path]::GetFullPath([string]$config.StagingRoot).TrimEnd('\')
        $recoveryFull = [IO.Path]::GetFullPath([string]$config.RecoveryRoot).TrimEnd('\')
        $dataFull = [IO.Path]::GetFullPath((Join-Path $stagingFull 'DATA')).TrimEnd('\')
        if ($recoveryFull.Equals($dataFull, [StringComparison]::OrdinalIgnoreCase) -or
            $recoveryFull.StartsWith($dataFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'RecoveryRoot must not be inside StagingRoot\DATA. Choose the same staging root or a separate folder.'
        }
    }
    catch {
        if ($_.Exception.Message -like 'RecoveryRoot must not be inside*') { throw }
        throw ('Invalid StagingRoot/RecoveryRoot path: ' + $_.Exception.Message)
    }
    return $config
}

function Get-TargetDisk {
    param([Parameter(Mandatory = $true)]$Config)
    $serial = [string]$Config.TargetSerial
    $matches = @(Get-CimInstance Win32_DiskDrive -ErrorAction Stop |
        Where-Object { $_.SerialNumber.Trim() -eq $serial })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one disk with serial $serial; found $($matches.Count)."
    }
    $disk = $matches[0]
    if ($disk.Model -ne $Config.TargetModel) {
        throw "Target model mismatch. Expected $($Config.TargetModel), actual $($disk.Model)."
    }
    if ($disk.FirmwareRevision -ne $Config.TargetFirmware) {
        throw "Target firmware mismatch. Expected $($Config.TargetFirmware), actual $($disk.FirmwareRevision)."
    }
    if ($disk.Index -ne [int]$Config.TargetDiskNumber) {
        throw "Target disk number changed. Expected $($Config.TargetDiskNumber), actual $($disk.Index)."
    }
    if ($disk.Size -le 0) {
        throw 'Target disk size is zero.'
    }
    $partitions = @(Get-CimInstance Win32_DiskPartition -ErrorAction SilentlyContinue |
        Where-Object DiskIndex -eq $disk.Index)
    if (@($partitions | Where-Object { $_.Bootable -or $_.BootPartition }).Count -gt 0) {
        throw 'Target disk contains a bootable/boot partition. Refusing to continue.'
    }
    return [pscustomobject]@{
        Disk = $disk
        Partitions = $partitions
        Serial = $disk.SerialNumber.Trim()
        Model = $disk.Model
        Firmware = $disk.FirmwareRevision
        Size = [int64]$disk.Size
        Index = [int]$disk.Index
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash } catch { return $null }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $Object | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-StepLog {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Message
    )
    $line = '[{0}] {1}' -f (Get-Date).ToString('o'), $Message
    [IO.File]::AppendAllText($LogPath, $line + "`r`n", $script:Utf8NoBom)
    Write-Host $line
}

function Format-Bytes {
    param([double]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N1} GiB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MiB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N1} KiB' -f ($Bytes / 1KB)) }
    return ('{0:N0} B' -f $Bytes)
}

function Format-Duration {
    param($Seconds)
    if ($null -eq $Seconds) { return 'n/a' }
    $v = [double]$Seconds
    if ([double]::IsNaN($v) -or $v -lt 0) { return 'n/a' }
    $ts = [TimeSpan]::FromSeconds($v)
    if ($ts.TotalHours -ge 1) { return ('{0}h {1}m' -f [int][math]::Floor($ts.TotalHours), $ts.Minutes) }
    if ($ts.TotalMinutes -ge 1) { return ('{0}m {1}s' -f [int][math]::Floor($ts.TotalMinutes), $ts.Seconds) }
    return ('{0}s' -f [int]$ts.TotalSeconds)
}

function Test-CancelFlag {
    param($Config)
    if (-not [string]::IsNullOrWhiteSpace([string]$Config.CancelFlag) -and
        (Test-Path -LiteralPath ([string]$Config.CancelFlag))) {
        throw "Cancel flag exists: $($Config.CancelFlag)"
    }
}

function Invoke-DiskPartFile {
    <#
      Runs diskpart /s <temp file>. This avoids the stdin timing race.
      The caller is responsible for reviewing the commands.
    #>
    param(
        [Parameter(Mandatory = $true)][string[]]$Commands
    )
    $diskpart = Join-Path $env:SystemRoot 'System32\diskpart.exe'
    $file = Join-Path $env:TEMP ('diskpart-' + [guid]::NewGuid().ToString('N') + '.txt')
    $Commands | Set-Content -LiteralPath $file -Encoding ASCII
    try {
        $output = & $diskpart /s $file 2>&1 | Out-String
        $code = $LASTEXITCODE
    }
    finally {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) {
        throw "DiskPart failed with exit code $code.`n$output"
    }
    if ($output -match '(?i)(Virtual Disk Service error|没有选择磁盘|错误|失败)') {
        throw "DiskPart reported an error.`n$output"
    }
    return $output
}

function Get-ChildItemSafe {
    param([string]$Root)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Root -Force -Recurse -ErrorAction SilentlyContinue)
}
