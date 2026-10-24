@echo off
dotnet run --project "%~dp0setup-tests\FlightCompanion.XPilot.Setup.Tests.csproj" --configuration Release
exit /b %ERRORLEVEL%
