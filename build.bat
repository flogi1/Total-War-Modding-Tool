@echo off
setlocal
cd /d "%~dp0"
dotnet build TotalWarModTool.slnx
set RESULT=%ERRORLEVEL%
rem Pause only when started by double-click, so the window stays readable.
echo %cmdcmdline% | find /i "%~nx0" >nul && pause
exit /b %RESULT%