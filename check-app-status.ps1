# LanDesk Application Status Checker
# Checks if the application is running and shows log information

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "LanDesk Application Status Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if app is running
$appProcess = Get-Process -Name "LanDesk" -ErrorAction SilentlyContinue
if ($appProcess) {
    Write-Host "LanDesk application is RUNNING" -ForegroundColor Green
    Write-Host "  Process ID: $($appProcess.Id)" -ForegroundColor Gray
    Write-Host "  Memory: $([math]::Round($appProcess.WorkingSet64 / 1MB, 2)) MB" -ForegroundColor Gray
}
else {
    Write-Host "LanDesk application is NOT running" -ForegroundColor Red
}

Write-Host ""

# Check if service is running
$service = Get-Service -Name "LanDesk Remote Desktop Service" -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq 'Running') {
        Write-Host "LanDesk Service is RUNNING" -ForegroundColor Green
    }
    else {
        Write-Host "LanDesk Service is installed but NOT running" -ForegroundColor Yellow
        Write-Host "  Status: $($service.Status)" -ForegroundColor Gray
    }
}
else {
    Write-Host "LanDesk Service is NOT installed" -ForegroundColor Yellow
}

Write-Host ""

# Check log files
$logDir = Join-Path $env:LOCALAPPDATA "LanDesk\Logs"
Write-Host "Log Directory: $logDir" -ForegroundColor Cyan

if (Test-Path $logDir) {
    $logFiles = Get-ChildItem -Path $logDir -Filter "LanDesk_*.log" | Sort-Object LastWriteTime -Descending
    if ($logFiles) {
        Write-Host "Found $($logFiles.Count) log file(s)" -ForegroundColor Green
        Write-Host ""
        Write-Host "Latest log file:" -ForegroundColor Yellow
        $latestLog = $logFiles[0]
        Write-Host "  File: $($latestLog.Name)" -ForegroundColor Gray
        Write-Host "  Size: $([math]::Round($latestLog.Length / 1KB, 2)) KB" -ForegroundColor Gray
        Write-Host "  Modified: $($latestLog.LastWriteTime)" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Last 20 lines:" -ForegroundColor Yellow
        Write-Host "----------------------------------------" -ForegroundColor Gray
        Get-Content $latestLog.FullName -Tail 20
        Write-Host "----------------------------------------" -ForegroundColor Gray
    }
    else {
        Write-Host "No log files found" -ForegroundColor Yellow
    }
}
else {
    Write-Host "Log directory does not exist" -ForegroundColor Yellow
    Write-Host "  This might mean the app has never started successfully" -ForegroundColor Gray
}

Write-Host ""

# Check service log (services run as SYSTEM, so logs are in ProgramData)
$serviceLogDir = Join-Path $env:PROGRAMDATA "LanDesk\Logs"
$serviceLog = Join-Path $serviceLogDir "LanDesk_$(Get-Date -Format 'yyyyMMdd').log"
if (Test-Path $serviceLog) {
    Write-Host "Service Log: $serviceLog" -ForegroundColor Cyan
    Write-Host "Last 20 lines:" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Gray
    Get-Content $serviceLog -Tail 20 -ErrorAction SilentlyContinue
    Write-Host "----------------------------------------" -ForegroundColor Gray
}
else {
    Write-Host "Service log not found at: $serviceLog" -ForegroundColor Yellow
    Write-Host "  (Service may not be running or hasn't logged yet)" -ForegroundColor Gray
    
    # Also check old location
    $oldServiceLog = Join-Path $env:PROGRAMDATA "LanDesk\service.log"
    if (Test-Path $oldServiceLog) {
        Write-Host "Found old service log: $oldServiceLog" -ForegroundColor Cyan
        Get-Content $oldServiceLog -Tail 10 -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "To view full logs, run: view-logs.bat" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
