@echo off
setlocal
call "%~dp0Build.cmd"
if errorlevel 1 exit /b 1
dotnet run --project "%~dp0tests\FlightCompanion.XPilot.Tests.csproj" --configuration Release
exit /b %ERRORLEVEL%
