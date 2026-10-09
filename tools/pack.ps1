param([string]$Configuration = "Release", [string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "src\TbtHttpBridge\bin\$Configuration\net9.0"
$package = Join-Path $root "dist\TbtHttpBridge-$Version"
$zip = Join-Path $root "dist\TbtHttpBridge-$Version.zip"

if (Test-Path $package) { Remove-Item $package -Recurse -Force }
New-Item -ItemType Directory -Path $package -Force | Out-Null

# Whitelist only: plugin.json + plugin dll + its deps.json.
# The contract assembly (ThinkBookToolkit.PluginApi.dll) MUST NOT be packaged.
Copy-Item -LiteralPath (Join-Path $root "plugin.json") -Destination (Join-Path $package "plugin.json") -Force
foreach ($name in @("TbtHttpBridge.dll", "TbtHttpBridge.deps.json")) {
    $source = Join-Path $build $name
    if (-not (Test-Path $source)) { throw "Missing build output: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $package $name) -Force
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force

if (Test-Path (Join-Path $package "ThinkBookToolkit.PluginApi.dll")) { throw "Contract assembly must not be packaged." }

Write-Host "Package folder: $package"
foreach ($file in (Get-ChildItem -LiteralPath $package -File | Sort-Object Name)) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Write-Host ("  {0,-28} {1,8}  {2}" -f $file.Name, $file.Length, $hash)
}
Write-Host "Archive: $zip"
