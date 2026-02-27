# Test Run Script for LanDesk
# Runs the application and captures any errors

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "LanDesk Test Run" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$exePath = "publish\win-x64\LanDesk.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Error: Executable not found at $exePath" -ForegroundColor Red
    Write-Host "Please run publish.bat first to build the application." -ForegroundColor Yellow
    exit 1
}

Write-Host "Found executable: $exePath" -ForegroundColor Green
Write-Host ""
Write-Host "Checking ports..." -ForegroundColor Yellow

# Check if ports are in use
$udpPort = Get-NetUDPEndpoint -LocalPort 54987 -ErrorAction SilentlyContinue
$tcpPort = Get-NetTCPConnection -LocalPort 54988 -State Listen -ErrorAction SilentlyContinue

if ($udpPort) {
    Write-Host "Warning: UDP port 54987 is already in use" -ForegroundColor Yellow
} else {
    Write-Host "UDP port 54987 is available" -ForegroundColor Green
}

if ($tcpPort) {
    Write-Host "Warning: TCP port 54988 is already in use" -ForegroundColor Yellow
} else {
    Write-Host "TCP port 54988 is available" -ForegroundColor Green
}

Write-Host ""
Write-Host "Starting application..." -ForegroundColor Yellow
Write-Host ""

try {
    & $exePath
    Write-Host ""
    Write-Host "Application exited normally." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "Error running application: $_" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}
