@echo off
echo ========================================
echo Building LanDesk Server Executable
echo ========================================
echo.

REM Check if Python is installed
python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python is not installed or not in PATH
    pause
    exit /b 1
)

REM Check if PyInstaller is installed
python -c "import PyInstaller" >nul 2>&1
if errorlevel 1 (
    echo Installing PyInstaller...
    pip install pyinstaller
    if errorlevel 1 (
        echo ERROR: Failed to install PyInstaller
        pause
        exit /b 1
    )
)

echo Building executable...
echo.

REM Build with PyInstaller
pyinstaller --onefile --name landesk_server --clean landesk_server.py

if errorlevel 1 (
    echo.
    echo ========================================
    echo Build failed!
    echo ========================================
    pause
    exit /b 1
)

echo.
echo ========================================
echo Build successful!
echo ========================================
echo Executable location: dist\landesk_server.exe
echo.
pause
