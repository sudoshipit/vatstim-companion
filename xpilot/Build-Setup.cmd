@echo off
dotnet publish "%~dp0setup\FlightCompanion.XPilot.Setup.csproj" --configuration Release --nologo --no-self-contained --output "%~dp0release\setup"
exit /b %ERRORLEVEL%
