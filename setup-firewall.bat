@echo off
REM LanDesk Firewall Configuration Script (Batch version)
REM This script will run the PowerShell script with administrator privileges

echo ========================================
echo LanDesk Firewall Configuration
echo ========================================
echo.

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This script requires administrator privileges.
    echo.
    echo Attempting to elevate...
    echo.
    powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0setup-firewall.ps1\"'"
    exit /b
)

REM Run PowerShell script
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-firewall.ps1"

pause
