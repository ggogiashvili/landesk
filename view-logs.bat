@echo off
REM LanDesk Log Viewer
REM Opens the log folder and shows the latest log entries

echo ========================================
echo LanDesk Log Viewer
echo ========================================
echo.

set LOG_DIR=%LOCALAPPDATA%\LanDesk\Logs
set SERVICE_LOG=%PROGRAMDATA%\LanDesk\service.log

echo Main Application Logs: %LOG_DIR%
echo Service Log: %SERVICE_LOG%
echo.

REM Create log directory if it doesn't exist
if not exist "%LOG_DIR%" (
    echo Log directory does not exist yet. Creating...
    mkdir "%LOG_DIR%"
)

REM Open log folder
echo Opening log folder...
explorer "%LOG_DIR%"

REM Show latest log entries
echo.
echo ========================================
echo Latest Application Log Entries:
echo ========================================
echo.

REM Get today's log file
for /f "tokens=2 delims==" %%I in ('wmic os get localdatetime /value') do set datetime=%%I
set TODAY=%datetime:~0,8%
set LOG_FILE=%LOG_DIR%\LanDesk_%TODAY%.log

if exist "%LOG_FILE%" (
    echo Showing last 30 lines of: %LOG_FILE%
    echo.
    powershell -Command "Get-Content '%LOG_FILE%' -Tail 30"
) else (
    echo No log file found for today.
    echo Looking for any log files...
    dir /b "%LOG_DIR%\LanDesk_*.log" 2>nul
    if errorlevel 1 (
        echo No log files found.
    ) else (
        echo.
        echo Showing latest log file:
        for /f "delims=" %%F in ('dir /b /o-d "%LOG_DIR%\LanDesk_*.log" 2^>nul') do (
            powershell -Command "Get-Content '%LOG_DIR%\%%F' -Tail 30"
            goto :found
        )
    )
)

:found
echo.
echo ========================================
echo Service Log:
echo ========================================
echo.

if exist "%SERVICE_LOG%" (
    echo Showing last 20 lines of service log:
    echo.
    powershell -Command "Get-Content '%SERVICE_LOG%' -Tail 20"
) else (
    echo Service log not found. Service may not be installed.
)

echo.
echo ========================================
echo.
echo Log folder opened in Explorer.
echo Press any key to exit...
pause >nul
