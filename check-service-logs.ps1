Write-Host "=== Checking LanDesk Service Status ===" -ForegroundColor Cyan
$service = Get-Service -Name "LanDesk Service" -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Service Status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq 'Running') { 'Green' } else { 'Yellow' })
    Write-Host "Service Name: $($service.DisplayName)"
} else {
    Write-Host "Service not found!" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Service Log Location ===" -ForegroundColor Cyan
$logPath = "C:\ProgramData\LanDesk\Logs\LanDesk_20260121.log"
if (Test-Path $logPath) {
    $logFile = Get-Item $logPath
    Write-Host "Log file exists: $logPath" -ForegroundColor Green
    Write-Host "File size: $($logFile.Length) bytes"
    Write-Host "Last modified: $($logFile.LastWriteTime)"
    Write-Host ""
    Write-Host "=== Last 100 lines of service log ===" -ForegroundColor Cyan
    Get-Content $logPath -Tail 100
} else {
    Write-Host "Log file not found: $logPath" -ForegroundColor Yellow
    Write-Host "Checking if directory exists..."
    $logDir = "C:\ProgramData\LanDesk\Logs"
    if (Test-Path $logDir) {
        Write-Host "Directory exists. Files:" -ForegroundColor Green
        Get-ChildItem $logDir | Select-Object Name, @{Name="Size (bytes)";Expression={$_.Length}}, LastWriteTime | Format-Table -AutoSize
    } else {
        Write-Host "Directory does not exist!" -ForegroundColor Red
        Write-Host "This might mean the service has never started or logged anything."
    }
}

Write-Host ""
Write-Host "=== Windows Event Log (Last 10 entries) ===" -ForegroundColor Cyan
try {
    Get-EventLog -LogName Application -Source "LanDesk Service" -Newest 10 -ErrorAction SilentlyContinue | 
        Select-Object TimeGenerated, EntryType, Message | Format-Table -AutoSize -Wrap
} catch {
    Write-Host "No event log entries found for 'LanDesk Service'" -ForegroundColor Yellow
}
