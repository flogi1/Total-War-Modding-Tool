@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\TotalWarModTool.App
set RESULT=%ERRORLEVEL%
rem Pause only when started by double-click, so the window stays readable.
echo %cmdcmdline% | find /i "%~nx0" >nul && pause
exit /b %RESULT%