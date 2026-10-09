param(
    [string]$Version = "1.1.0",
    [switch]$Execute
)

# Build -> test -> pack -> checksums -> (optionally) gh release create.
# Without -Execute it only prints the commands, so nothing is published by accident.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $root "dist\TbtHttpBridge-$Version"
$zip = Join-Path $root "dist\TbtHttpBridge-$Version.zip"
$sums = Join-Path $root "dist\SHA256SUMS-v$Version.txt"

Write-Host "== 1/3 build ==" -ForegroundColor Cyan
& (Join-Path $root 'build.cmd')
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "== 2/3 test ==" -ForegroundColor Cyan
& (Join-Path $root 'test.cmd')
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

Write-Host "== 3/3 pack ==" -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\pack.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { throw "pack failed" }

$lines = @()
foreach ($file in (Get-ChildItem -LiteralPath $package -File | Sort-Object Name)) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $lines += ("{0}  {1}" -f $hash, $file.Name)
}
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
$lines += ("{0}  {1}" -f $zipHash, (Split-Path -Leaf $zip))
$lines | Set-Content -LiteralPath $sums -Encoding ASCII

Write-Host "Checksums written: $sums" -ForegroundColor Green
$lines | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "Note: the DLL hash is reproducible (Deterministic build); the ZIP hash is not" -ForegroundColor Yellow
Write-Host "(Compress-Archive stores file timestamps). Verify a release by the DLL hash." -ForegroundColor Yellow
Write-Host ""

$notes = Join-Path $root "docs\release-notes-v$Version.md"
$command = 'gh release create v{0} "{1}" "{2}" --title "v{0}" --notes-file "{3}"' -f $Version, $zip, $sums, $notes

if ($Execute) {
    Write-Host "== publishing ==" -ForegroundColor Cyan
    Invoke-Expression $command
} else {
    Write-Host "Dry run. To publish:" -ForegroundColor Cyan
    Write-Host "  $command"
}
