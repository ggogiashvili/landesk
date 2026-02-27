@echo off
echo ========================================
echo LanDesk Discovery Server (Production)
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

REM Check if waitress is installed (production server for Windows)
python -c "import waitress" >nul 2>&1
if errorlevel 1 (
    echo Installing production server (waitress)...
    pip install waitress
    if errorlevel 1 (
        echo WARNING: Failed to install waitress. Server will use development mode.
        echo For production, install waitress: pip install waitress
    )
)

REM Check for port argument or environment variable
set SERVER_PORT=8080
if not "%1"=="" (
    set SERVER_PORT=%1
)
if not "%LANDESK_SERVER_PORT%"=="" (
    set SERVER_PORT=%LANDESK_SERVER_PORT%
)

REM Configuration
set LANDESK_USE_PRODUCTION_SERVER=true
set LANDESK_THREADS=8
set LANDESK_WORKERS=4

REM Check if running as admin (for port 80)
if "%SERVER_PORT%"=="80" (
    echo.
    echo WARNING: Port 80 requires Administrator privileges!
    echo If you see permission errors, run this script as Administrator
    echo (Right-click -> Run as Administrator)
    echo.
    echo Alternatively, use port 8080 (default):
    echo   start_server_production.bat 8080
    echo.
    timeout /t 3 >nul
)

echo Starting production server on port %SERVER_PORT%...
echo Configuration:
echo   Threads per worker: %LANDESK_THREADS%
echo   Workers: %LANDESK_WORKERS%
echo   Production mode: Enabled
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
        echo Try running this script as Administrator, or use port 8080:
        echo   start_server_production.bat 8080
    ) else (
        echo Check the error message above for details.
    )
    echo.
    pause
    exit /b 1
)

pause
