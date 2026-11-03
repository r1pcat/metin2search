@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-scanner.ps1"
if errorlevel 1 (
 echo Tarayici acilirken hata olustu.
 if exist "%~dp0scanner-data\launcher-error.txt" type "%~dp0scanner-data\launcher-error.txt"
 pause
)
if not errorlevel 1 timeout /t 8 >nul
