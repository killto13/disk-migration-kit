#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $root 'dist' }
if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found: $csc" }
$source = Join-Path $PSScriptRoot 'EDiskMigration.cs'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$icon = Join-Path $PSScriptRoot 'app.ico'
$exe = Join-Path $OutputDir 'EDiskMigration.exe'
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/codepage:65001',
    ('/win32manifest:' + $manifest),
    ('/out:' + $exe),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Management.dll',
    $source
)
if (Test-Path -LiteralPath $icon) { $cscArgs += ('/win32icon:' + $icon) }
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Build output missing: $exe" }
Get-Item -LiteralPath $exe | Select-Object FullName, Length, LastWriteTime | Format-List
