#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProgressCsv,
    [Parameter(Mandatory = $true)][int64]$TotalBytes,
    [Parameter(Mandatory = $true)][int64]$TotalFiles,
    [int]$RefreshSeconds = 2,
    [int]$RateWindowMinutes = 5,
    [switch]$Once,
    [switch]$NoClear
)

$ErrorActionPreference = 'Continue'

function Format-Bytes {
    param([double]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N1} GiB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MiB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N1} KiB' -f ($Bytes / 1KB)) }
    return ('{0:N0} B' -f $Bytes)
}
function Format-Speed {
    param([double]$BytesPerSec)
    if ($BytesPerSec -ge 1MB) { return ('{0:N1} MiB/s' -f ($BytesPerSec / 1MB)) }
    if ($BytesPerSec -ge 1KB) { return ('{0:N1} KiB/s' -f ($BytesPerSec / 1KB)) }
    return ('{0:N0} B/s' -f $BytesPerSec)
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

function Read-Rows {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }
    try { $lines = Get-Content -LiteralPath $Path -Tail 120 -ErrorAction Stop } catch { return @() }
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($line in $lines) {
        if ($line -match '^(\d{4}-\d{2}-\d{2}T[^,]+),(\d+),(\d+),(\d+)\s*$') {
            try {
                $rows.Add([pscustomobject]@{
                    Time = [datetimeoffset]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture).LocalDateTime
                    Bytes = [int64]$Matches[2]
                    Files = [int64]$Matches[3]
                    Mismatches = [int]$Matches[4]
                })
            }
            catch {}
        }
    }
    return $rows.ToArray()
}

try {
    while ($true) {
        $rows = @(Read-Rows $ProgressCsv)
        $last = if ($rows.Count -gt 0) { $rows[$rows.Count - 1] } else { $null }
        $first = if ($rows.Count -gt 0) { $rows[0] } else { $null }
        $bytesPct = 0.0; $filesPct = 0.0; $bytesRate = 0.0; $filesRate = 0.0
        $etaBytes = $null; $etaFiles = $null; $eta = $null; $mismatch = 'n/a'
        if ($last) {
            $bytesPct = [math]::Min(1.0, [double]$last.Bytes / $TotalBytes)
            $filesPct = [math]::Min(1.0, [double]$last.Files / $TotalFiles)
            $mismatch = [string]$last.Mismatches
            $window = @($rows | Where-Object { $_.Time -ge $last.Time.AddMinutes(-1 * $RateWindowMinutes) })
            if ($window.Count -lt 2) { $window = @($rows) }
            $w0 = $window[0]; $w1 = $window[$window.Count - 1]
            $sec = ($w1.Time - $w0.Time).TotalSeconds
            if ($sec -gt 0) {
                $bytesRate = ($w1.Bytes - $w0.Bytes) / $sec
                $filesRate = ($w1.Files - $w0.Files) / $sec
            }
            if ($bytesRate -gt 0) { $etaBytes = ([math]::Max([int64]0, $TotalBytes - $last.Bytes)) / $bytesRate }
            if ($filesRate -gt 0) { $etaFiles = ([math]::Max([int64]0, $TotalFiles - $last.Files)) / $filesRate }
            if ($null -ne $etaBytes -and $null -ne $etaFiles) { $eta = [math]::Max($etaBytes, $etaFiles) }
            elseif ($null -ne $etaBytes) { $eta = $etaBytes }
            elseif ($null -ne $etaFiles) { $eta = $etaFiles }
        }

        $barWidth = 40
        $filled = [int][math]::Round($bytesPct * $barWidth)
        $bar = ('[' + ('#' * $filled) + ('-' * ($barWidth - $filled)) + ']')
        if (-not $NoClear) { Clear-Host }
        Write-Host '============================================================' -ForegroundColor Cyan
        Write-Host '  Tree comparison progress' -ForegroundColor Cyan
        Write-Host ('  CSV: {0}' -f $ProgressCsv) -ForegroundColor DarkGray
        Write-Host '============================================================' -ForegroundColor Cyan
        Write-Host ('  {0}  {1:N1}%' -f $bar, ($bytesPct * 100)) -ForegroundColor Green
        if ($last) {
            Write-Host ('  Files: {0:N0} / {1:N0} ({2:N1}%)' -f $last.Files, $TotalFiles, ($filesPct * 100))
            Write-Host ('  Bytes: {0} / {1} ({2:N1}%)' -f (Format-Bytes $last.Bytes), (Format-Bytes $TotalBytes), ($bytesPct * 100))
            Write-Host ('  Speed: {0}   {1:N1} files/s' -f (Format-Speed $bytesRate), $filesRate) -ForegroundColor Green
            Write-Host ('  ETA: {0} (bytes {1} / files {2})' -f (Format-Duration $eta), (Format-Duration $etaBytes), (Format-Duration $etaFiles)) -ForegroundColor Yellow
            Write-Host ('  Last update: {0}' -f $last.Time.ToString('yyyy-MM-dd HH:mm:ss'))
            Write-Host ('  Mismatches: {0}' -f $mismatch) -ForegroundColor $(if ($last.Mismatches -gt 0) { 'Red' } else { 'Gray' })
        }
        else {
            Write-Host '  Waiting for the first progress row...'
        }
        Write-Host ''
        Write-Host '  Ctrl+C exits this display only.' -ForegroundColor DarkGray
        Write-Host '============================================================' -ForegroundColor Cyan
        if ($Once) { break }
        Start-Sleep -Seconds ([math]::Max(1, $RefreshSeconds))
    }
}
finally {
    Write-Host ''
    Write-Host 'Display exited. The comparison itself is not stopped.' -ForegroundColor DarkGray
}
