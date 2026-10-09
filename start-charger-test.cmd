@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-charger-test.ps1" %*
if errorlevel 1 (
  echo.
  echo Nisja deshtoi. Lexo gabimin dhe loget me siper.
  pause
  endlocal
  exit /b 1
)
endlocal
