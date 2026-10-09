@echo off
setlocal
set DOTNET_CLI_HOME=%~dp0.dotnet-home
set NUGET_PACKAGES=%~dp0.nuget
set DOTNET_NOLOGO=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_CLI_UI_LANGUAGE=en
dotnet build "%~dp0tests\HostSim\HostSim.csproj" -c Release -m:1 -nodeReuse:false -v:q
if errorlevel 1 exit /b 1
dotnet "%~dp0tests\HostSim\bin\Release\net9.0\HostSim.dll" %*
exit /b %ERRORLEVEL%
