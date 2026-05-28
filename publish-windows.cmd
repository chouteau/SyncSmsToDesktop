@echo off
setlocal

set "PROJECT=%~dp0src\SmsSyncMaui\SmsSyncMaui.csproj"
set "OUTPUT=E:\Tools\SyncSmsToDesktop"

if not exist "%PROJECT%" (
	echo Project not found: %PROJECT%
	exit /b 1
)

echo Publishing SmsSyncMaui to %OUTPUT%...
dotnet publish "%PROJECT%" -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -o "%OUTPUT%" %*
if errorlevel 1 exit /b %errorlevel%

echo.
echo Publish completed: %OUTPUT%
endlocal
