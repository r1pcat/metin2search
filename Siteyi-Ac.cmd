@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-site.ps1"
if errorlevel 1 (
 echo.
 echo Site acilamadi. Yukaridaki mesaji okuyun.
 pause
 exit /b 1
)
echo Site aciliyor, birkac saniye bekleyin...
timeout /t 8 >nul
powershell.exe -NoProfile -Command "try{Invoke-WebRequest http://127.0.0.1:4180/api/health -UseBasicParsing -TimeoutSec 8 | Out-Null;exit 0}catch{exit 1}"
if errorlevel 1 (
 echo.
 echo Site yanit vermiyor. Son hata kayitlari:
 if exist "%~dp0market-draft\data\live-server.err.log" powershell.exe -NoProfile -Command "Get-Content '%~dp0market-draft\data\live-server.err.log' -Tail 15"
 pause
 exit /b 1
)
start "" http://127.0.0.1:4180/pazar