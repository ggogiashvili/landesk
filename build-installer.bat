@echo off
REM LanDesk Installer Builder (Batch version)

echo ========================================
echo LanDesk Installer Builder
echo ========================================
echo.

REM Check if files are in use
echo Checking if LanDesk is running...
tasklist /FI "IMAGENAME eq LanDesk.exe" 2>NUL | find /I /N "LanDesk.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo WARNING: LanDesk.exe is currently running!
    echo Attempting to close it automatically...
    taskkill /F /IM LanDesk.exe 2>NUL
    ping 127.0.0.1 -n 3 >nul
    echo.
)

REM First, publish the application
echo Step 1: Publishing application...
call publish.bat
if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Step 2: Creating installer...
powershell -NoProfile -ExecutionPolicy Bypass -File "create-installer.ps1"

if errorlevel 1 (
    echo Installer creation failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo Installer created successfully!
echo ========================================
echo.
echo Location: installer\LanDeskSetup.exe
echo.
pause
