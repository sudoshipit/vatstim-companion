@echo off
setlocal
call "%~dp0Build.cmd" %1
if errorlevel 1 exit /b 1
set "FC_COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
"%FC_COMPILER%" /nologo /target:winexe /platform:anycpu /warnaserror+ /optimize+ /out:"%~dp0setup\FlightCompanion.Setup.exe" /resource:"%~dp0release\FlightCompanion.Plugin.dll",FlightCompanion.Plugin.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%~dp0src\Settings.cs" "%~dp0setup\Core.cs" "%~dp0setup\Program.cs"
exit /b %ERRORLEVEL%
