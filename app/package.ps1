#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $root 'dist' }
$stage = Join-Path $OutputDir 'EDiskMigration-portable'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

& (Join-Path $PSScriptRoot 'build.ps1') -OutputDir $OutputDir

foreach ($item in @('scripts', 'docs', 'examples')) {
    Copy-Item -LiteralPath (Join-Path $root $item) -Destination $stage -Recurse -Force
}
foreach ($file in @('README.md', 'README.zh-CN.md', 'README.en.md', 'LICENSE', 'SECURITY.md')) {
    $p = Join-Path $root $file
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $stage -Force }
}
Copy-Item -LiteralPath (Join-Path $OutputDir 'EDiskMigration.exe') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README_APP.md') -Destination $stage -Force
$appReadmeEn = Join-Path $PSScriptRoot 'README_APP.en.md'
if (Test-Path -LiteralPath $appReadmeEn) { Copy-Item -LiteralPath $appReadmeEn -Destination $stage -Force }

$zip = Join-Path $OutputDir ('EDiskMigration-portable-' + (Get-Date -Format 'yyyyMMdd') + '.zip')
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Get-Item -LiteralPath $zip | Select-Object FullName, Length, @{n='MiB';e={[math]::Round($_.Length/1MB,2)}} | Format-List
