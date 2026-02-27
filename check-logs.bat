@echo off
REM Quick log checker for LanDesk

echo ========================================
echo LanDesk Log Checker
echo ========================================
echo.

set LOG_DIR=%LOCALAPPDATA%\LanDesk\Logs
set ERROR_LOG=%LOCALAPPDATA%\LanDesk\startup-error.txt
set TEMP_LOG=%TEMP%\LanDesk.log

echo Checking for log files...
echo.

if exist "%LOG_DIR%" (
    echo Found log directory: %LOG_DIR%
    echo.
    echo Latest log files:
    dir /b /o-d "%LOG_DIR%\LanDesk_*.log" 2>nul | head -n 1
    echo.
    
    for /f "delims=" %%F in ('dir /b /o-d "%LOG_DIR%\LanDesk_*.log" 2^>nul') do (
        echo ========================================
        echo Showing last 30 lines of: %%F
        echo ========================================
        powershell -Command "Get-Content '%LOG_DIR%\%%F' -Tail 30"
        goto :found
    )
) else (
    echo Log directory does not exist: %LOG_DIR%
)

:found
echo.
echo ========================================
echo Checking for startup error file...
echo ========================================
echo.

if exist "%ERROR_LOG%" (
    echo Found startup error file!
    echo.
    type "%ERROR_LOG%"
) else (
    echo No startup error file found.
)

echo.
echo ========================================
echo Checking for startup debug file...
echo ========================================
echo.

set DEBUG_LOG=%LOCALAPPDATA%\LanDesk\startup-debug.txt

if exist "%DEBUG_LOG%" (
    echo Found startup debug file!
    echo.
    type "%DEBUG_LOG%"
) else (
    echo No startup debug file found.
)

echo.
echo ========================================
echo Checking temp log...
echo ========================================
echo.

if exist "%TEMP_LOG%" (
    echo Found temp log file!
    echo.
    type "%TEMP_LOG%"
) else (
    echo No temp log file found.
)

echo.
echo ========================================
echo Checking startup trace file...
echo ========================================
echo.

set STARTUP_TRACE=%TEMP%\LanDesk-startup.txt

if exist "%STARTUP_TRACE%" (
    echo Found startup trace file!
    echo.
    type "%STARTUP_TRACE%"
) else (
    echo No startup trace file found.
    echo This means the app crashed before any code ran.
)

echo.
echo ========================================
echo.
pause
