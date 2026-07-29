@echo off
setlocal enabledelayedexpansion

REM WireSock hizmetini onay almadan durdur
sc stop "wiresock-client-service" >nul 2>&1

REM 5 saniye bekle
timeout /t 5 /nobreak >nul

REM WireSock hizmetini tekrar başlat
sc start "wiresock-client-service" >nul 2>&1

REM Başlatma sonrası 5 saniye bekle
timeout /t 5 /nobreak >nul

set "attempt=1"
:retry
REM Hizmet durumunu kontrol et
for /f "tokens=3" %%a in ('sc query "wiresock-client-service" ^| findstr "STATE"') do set state=%%a

if /i "!state!"=="RUNNING" (
    goto :done
) else (
    if !attempt! lss 3 (
        set /a attempt+=1
        sc start "wiresock-client-service" >nul 2>&1
        timeout /t 5 /nobreak >nul
        goto :retry
    )
)

:done
endlocal