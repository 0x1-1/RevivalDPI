@echo off
setlocal

cd /d "%~dp0"

echo Building RevivalDPI C# WPF application...

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo Error: .NET SDK not found. Install .NET 8 SDK or later.
    exit /b 1
)

dotnet restore "RevivalDPI\RevivalDPI.csproj"
if errorlevel 1 (
    echo Error: dotnet restore failed.
    exit /b 1
)

dotnet build "RevivalDPI\RevivalDPI.csproj" -c Release
if errorlevel 1 (
    echo Error: dotnet build failed.
    exit /b 1
)

echo.
echo Build complete:
echo   RevivalDPI\bin\Release\net8.0-windows\RevivalDPI.exe
echo.
echo For release packaging, run:
echo   powershell -ExecutionPolicy Bypass -File publish_release.ps1
