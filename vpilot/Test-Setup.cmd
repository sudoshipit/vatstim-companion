@echo off
setlocal
call "%~dp0Build-Setup.cmd"
if errorlevel 1 exit /b 1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /warnaserror+ /out:"%~dp0setup\FlightCompanion.Setup.Tests.exe" /reference:"%~dp0setup\FlightCompanion.Setup.exe" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll "%~dp0setup\Tests.cs"
if errorlevel 1 exit /b 1
"%~dp0setup\FlightCompanion.Setup.Tests.exe"
exit /b %ERRORLEVEL%
