@echo off
setlocal
if not defined VPILOT_PATH set "VPILOT_PATH=%LOCALAPPDATA%\vPilot"
if not "%~1"=="" set "VPILOT_PATH=%~1"
call "%~dp0Build.cmd" "%VPILOT_PATH%"
if errorlevel 1 exit /b 1
if not exist "%~dp0tests\bin" mkdir "%~dp0tests\bin"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:anycpu /warnaserror+ /out:"%~dp0tests\bin\FlightCompanion.Tests.exe" /reference:"%~dp0release\FlightCompanion.Plugin.dll" /reference:"%VPILOT_PATH%\RossCarlson.Vatsim.Vpilot.Plugins.dll" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "%~dp0tests\Tests.cs"
if errorlevel 1 exit /b 1
copy /y "%~dp0release\FlightCompanion.Plugin.dll" "%~dp0tests\bin\FlightCompanion.Plugin.dll" >nul
copy /y "%VPILOT_PATH%\RossCarlson.Vatsim.Vpilot.Plugins.dll" "%~dp0tests\bin\RossCarlson.Vatsim.Vpilot.Plugins.dll" >nul
"%~dp0tests\bin\FlightCompanion.Tests.exe"
exit /b %ERRORLEVEL%
