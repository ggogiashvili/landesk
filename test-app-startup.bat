@echo off
REM Test LanDesk application startup
REM This will try to run the app and show any errors

echo ========================================
echo LanDesk Application Startup Test
echo ========================================
echo.

REM Check if installed version exists
set INSTALLED_APP=C:\Program Files\LanDesk\LanDesk.exe
set PUBLISHED_APP=%~dp0publish\win-x64\LanDesk.exe

if exist "%INSTALLED_APP%" (
    echo Found installed application: %INSTALLED_APP%
    echo.
    echo Attempting to start application...
    echo.
    "%INSTALLED_APP%"
    if errorlevel 1 (
        echo.
        echo ========================================
        echo Application exited with error code: %ERRORLEVEL%
        echo ========================================
    ) else (
        echo.
        echo Application started successfully (or closed normally)
    )
) else if exist "%PUBLISHED_APP%" (
    echo Found published application: %PUBLISHED_APP%
    echo.
    echo Attempting to start application...
    echo.
    "%PUBLISHED_APP%"
    if errorlevel 1 (
        echo.
        echo ========================================
        echo Application exited with error code: %ERRORLEVEL%
        echo ========================================
    ) else (
        echo.
        echo Application started successfully (or closed normally)
    )
) else (
    echo ERROR: Application not found!
    echo.
    echo Checked locations:
    echo   - %INSTALLED_APP%
    echo   - %PUBLISHED_APP%
    echo.
    echo Please build the application first or install it.
)

echo.
echo ========================================
echo Check for log files in:
echo %LOCALAPPDATA%\LanDesk\Logs\
echo ========================================
echo.
pause
