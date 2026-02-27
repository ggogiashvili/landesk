@echo off
echo ========================================
echo LanDesk Discovery Server
echo ========================================
echo.

REM Check if Python is installed
python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python is not installed or not in PATH
    echo Please install Python 3.7 or higher
    pause
    exit /b 1
)

REM Check if dependencies are installed
python -c "import flask" >nul 2>&1
if errorlevel 1 (
    echo Installing dependencies...
    pip install -r requirements.txt
    if errorlevel 1 (
        echo ERROR: Failed to install dependencies
        pause
        exit /b 1
    )
)

REM Note: Development mode - for production use start_server_production.bat
echo.
echo NOTE: This is development mode. For production with better performance:
echo   Use: start_server_production.bat
echo.

REM Check for port argument or environment variable
set SERVER_PORT=9061
if not "%1"=="" (
    set SERVER_PORT=%1
)
if not "%LANDESK_SERVER_PORT%"=="" (
    set SERVER_PORT=%LANDESK_SERVER_PORT%
)

REM Check if running as admin (for port 80)
if "%SERVER_PORT%"=="80" (
    echo.
    echo WARNING: Port 80 requires Administrator privileges!
    echo If you see permission errors, run this script as Administrator
    echo (Right-click -> Run as Administrator)
    echo.
    echo Alternatively, use port 9061 (default):
    echo   start_server.bat 9061
    echo.
    timeout /t 3 >nul
)

echo Starting server on port %SERVER_PORT%...
echo.
python landesk_server.py %SERVER_PORT%

if errorlevel 1 (
    echo.
    echo ========================================
    echo Server failed to start!
    echo ========================================
    echo.
    if "%SERVER_PORT%"=="80" (
        echo Port 80 requires Administrator privileges.
        echo Try running this script as Administrator, or use port 9061:
        echo   start_server.bat 9061
    ) else (
        echo Check the error message above for details.
    )
    echo.
    pause
    exit /b 1
)

pause
