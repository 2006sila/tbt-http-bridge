@echo off
setlocal
set DOTNET_CLI_HOME=%~dp0.dotnet-home
set NUGET_PACKAGES=%~dp0.nuget
set DOTNET_NOLOGO=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_UI_LANGUAGE=en

if not exist "%~dp0lib\ThinkBookToolkit.PluginApi.dll" if not exist "%~dp0third_party\PluginApi\Contracts.cs" (
  echo [build] Plugin contract not found.
  echo [build] Either keep third_party\PluginApi\Contracts.cs, or run:
  echo [build]   powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-contract.ps1
  exit /b 1
)

rem -m:1 is required: parallel restore/build silently fails for projects with
rem multiple project references and produces no output assembly.
dotnet msbuild "%~dp0src\TbtHttpBridge\TbtHttpBridge.csproj" -t:Restore,Build -p:Configuration=Release -m:1 -nodeReuse:false
exit /b %ERRORLEVEL%
