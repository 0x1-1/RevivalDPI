@echo off
chcp 65001 >nul
title DNS and DoH Settings Reset

echo ========================================
echo    DNS and DoH Settings Reset
echo ========================================
echo.
echo This script will reset DNS and DoH settings:
echo • Set DNS settings to automatic (DHCP)
echo • Disable DNS over HTTPS (DoH) feature
echo • Process all physical network adapters
echo.

REM Administrator permission check
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script requires administrator privileges!
    echo Please run the script as administrator.
    echo.
    pause
    exit /b 1
)

echo Administrator permission confirmed.
echo.

REM Run PowerShell script
echo Running PowerShell script...
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0reset_dns_settings.ps1"

echo.
echo ========================================
echo    Process completed!
echo ========================================
echo.
echo Script completed successfully!
