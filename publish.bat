@echo off
REM LanDesk Publishing Script (Batch version)
REM Creates a self-contained executable for Windows

setlocal

set CONFIGURATION=Release
set OUTPUT_PATH=.\publish

echo ========================================
echo LanDesk Publishing Script
echo ========================================
echo.

REM Clean previous builds
echo Cleaning previous builds...
if exist "%OUTPUT_PATH%" (
    echo Removing old build files...
    rmdir /s /q "%OUTPUT_PATH%"
    ping 127.0.0.1 -n 3 >nul
)

REM Restore packages
echo Restoring NuGet packages...
call dotnet restore
if errorlevel 1 (
    echo Failed to restore packages!
    exit /b 1
)

REM Build solution
echo Building solution...
call dotnet build -c %CONFIGURATION%
if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

REM Publish 64-bit application (without single-file to avoid bundle issues)
echo Publishing 64-bit application...
call dotnet publish LanDesk\LanDesk.csproj -c %CONFIGURATION% -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_PATH%\win-x64"
if errorlevel 1 (
    echo Publish failed!
    exit /b 1
)

REM Publish 64-bit service to separate directory first
echo Publishing 64-bit service...
timeout /t 1 /nobreak >nul
call dotnet publish LanDesk.Service\LanDesk.Service.csproj -c %CONFIGURATION% -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:PublishTrimmed=false -o "%OUTPUT_PATH%\win-x64-service"

REM Copy service files to main directory (exclude WPF DLLs to avoid overwriting)
if exist "%OUTPUT_PATH%\win-x64-service\LanDesk.Service.exe" (
    REM Use PowerShell to copy files excluding WPF DLLs
    powershell -Command "Get-ChildItem '%OUTPUT_PATH%\win-x64-service' -File | Where-Object { $_.Name -notlike 'WindowsBase.dll' -and $_.Name -notlike 'PresentationCore.dll' -and $_.Name -notlike 'PresentationFramework*.dll' -and $_.Name -notlike 'System.Windows*.dll' -and $_.Name -notlike 'System.Xaml.dll' -and $_.Name -notlike 'UIAutomation*.dll' } | Copy-Item -Destination '%OUTPUT_PATH%\win-x64' -Force"
    REM Copy subdirectories if they exist
    if exist "%OUTPUT_PATH%\win-x64-service\runtimes" (
        xcopy "%OUTPUT_PATH%\win-x64-service\runtimes" "%OUTPUT_PATH%\win-x64\runtimes\" /Y /E /I
    )
)

REM Publish 32-bit application
echo Publishing 32-bit application...
call dotnet publish LanDesk\LanDesk.csproj -c %CONFIGURATION% -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true -o "%OUTPUT_PATH%\win-x86"

REM Publish 32-bit service
echo Publishing 32-bit service...
call dotnet publish LanDesk.Service\LanDesk.Service.csproj -c %CONFIGURATION% -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true -o "%OUTPUT_PATH%\win-x86"

echo.
echo ========================================
echo Publishing Complete!
echo ========================================
echo.
echo Output location: %OUTPUT_PATH%
echo.
echo Files created:
echo   - %OUTPUT_PATH%\win-x64\LanDesk.exe (64-bit)
echo   - %OUTPUT_PATH%\win-x86\LanDesk.exe (32-bit)
echo.
echo These are self-contained executables that can run without .NET installed.
echo.

endlocal
