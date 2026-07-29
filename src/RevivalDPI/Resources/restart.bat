@echo off
taskkill /IM "RevivalDPI.exe" /F >nul 2>&1
timeout /t 3 /nobreak >nul
start "" "%~dp0..\RevivalDPI.exe"
exit
