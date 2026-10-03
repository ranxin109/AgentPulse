@echo off
setlocal
set "taskWidgetExe=%~dp0ProgressWidget.exe"
if not exist "%taskWidgetExe%" set "taskWidgetExe=%USERPROFILE%\Documents\Codex\progress-widget\ProgressWidget.exe"
if not exist "%taskWidgetExe%" (
  echo Task widget is not installed. Run install.ps1 first.
  pause
  exit /b 1
)
start "" "%taskWidgetExe%"
exit /b 0
