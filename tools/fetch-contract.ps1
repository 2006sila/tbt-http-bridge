param([string]$Source)

# Copies the host-provided contract assembly into lib\ so the plugin is compiled
# against exactly the version the local ThinkBook Toolkit installs.
# Optional: the default build already works without this (third_party/PluginApi source).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not $Source) {
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $candidates = @(
        (Join-Path $env:ProgramFiles 'ThinkBook Toolkit\ThinkBookToolkit.PluginApi.dll'),
        (Join-Path $programFilesX86 'ThinkBook Toolkit\ThinkBookToolkit.PluginApi.dll')
    )
    $Source = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

if (-not $Source -or -not (Test-Path -LiteralPath $Source)) {
    throw "ThinkBookToolkit.PluginApi.dll not found. Pass -Source <path> to point at your Toolkit folder."
}

$lib = Join-Path $root 'lib'
New-Item -ItemType Directory -Path $lib -Force | Out-Null
$destination = Join-Path $lib 'ThinkBookToolkit.PluginApi.dll'
Copy-Item -LiteralPath $Source -Destination $destination -Force

$info = Get-Item -LiteralPath $destination
Write-Host ("Contract copied: {0}" -f $Source)
Write-Host ("  -> {0}  ({1} bytes, FileVersion {2})" -f $destination, $info.Length, $info.VersionInfo.FileVersion)
Write-Host "Rebuild to compile against this copy."
