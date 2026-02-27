@echo off
REM Run LanDesk application with debug output

echo ========================================
echo LanDesk Debug Launcher
echo ========================================
echo.

set APP_PATH=%~dp0publish\win-x64\LanDesk.exe

if not exist "%APP_PATH%" (
    echo ERROR: Application not found at: %APP_PATH%
    echo.
    echo Please build the application first using: publish.bat
    pause
    exit /b 1
)

echo Starting LanDesk application...
echo Application path: %APP_PATH%
echo.
echo Note: Check the log file if the application doesn't start:
echo %LOCALAPPDATA%\LanDesk\Logs\
echo.

REM Run the application and capture any output
"%APP_PATH%" 2>&1

if errorlevel 1 (
    echo.
    echo ========================================
    echo Application exited with error code: %ERRORLEVEL%
    echo ========================================
    echo.
    echo Check the log file for details:
    echo %LOCALAPPDATA%\LanDesk\Logs\
    echo.
)

pause
