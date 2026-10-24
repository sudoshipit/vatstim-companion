@echo off
setlocal
if not defined VPILOT_PATH set "VPILOT_PATH=%LOCALAPPDATA%\vPilot"
if not "%~1"=="" set "VPILOT_PATH=%~1"
set "FC_COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "FC_SDK=%VPILOT_PATH%\RossCarlson.Vatsim.Vpilot.Plugins.dll"
if not exist "%FC_COMPILER%" (echo Missing .NET Framework C# compiler. & exit /b 1)
if not exist "%FC_SDK%" (echo Missing vPilot SDK. Pass the installed vPilot directory to Build.cmd. & exit /b 1)
if not exist "%~dp0release" mkdir "%~dp0release"
"%FC_COMPILER%" /nologo /target:library /platform:anycpu /warnaserror+ /optimize+ /out:"%~dp0release\FlightCompanion.Plugin.dll" /reference:"%FC_SDK%" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
copy /y "%~dp0FlightCompanion.Plugin.example.json" "%~dp0release\FlightCompanion.Plugin.example.json" >nul
echo Built release\FlightCompanion.Plugin.dll against your installed vPilot SDK.
echo No installation or network connection was performed.
exit /b 0
