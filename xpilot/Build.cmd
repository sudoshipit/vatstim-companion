@echo off
setlocal
dotnet build "%~dp0src\FlightCompanion.XPilot.csproj" --configuration Release --nologo
if errorlevel 1 exit /b 1
copy /y "%~dp0FlightCompanion.XPilot.example.json" "%~dp0release\FlightCompanion.XPilot.example.json" >nul
exit /b %ERRORLEVEL%
