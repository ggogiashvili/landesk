# LanDesk Log File Locations

## Main Application Logs

**Location:** `%LOCALAPPDATA%\LanDesk\Logs\`

**Full Path Examples:**
- Windows 10/11: `C:\Users\<YourUsername>\AppData\Local\LanDesk\Logs\`
- Example: `C:\Users\John\AppData\Local\LanDesk\Logs\LanDesk_20241215.log`

**Log File Format:**
- Files are named: `LanDesk_YYYYMMDD.log` (one file per day)
- Format: `[YYYY-MM-DD HH:mm:ss.fff] [LEVEL] Message`

**How to Access:**
1. Press `Win + R`
2. Type: `%LOCALAPPDATA%\LanDesk\Logs`
3. Press Enter

Or navigate manually:
- Open File Explorer
- Go to: `C:\Users\<YourUsername>\AppData\Local\LanDesk\Logs\`

## Windows Service Logs

**Location:** `%PROGRAMDATA%\LanDesk\service.log`

**Full Path:**
- `C:\ProgramData\LanDesk\service.log`

**How to Access:**
1. Press `Win + R`
2. Type: `%PROGRAMDATA%\LanDesk`
3. Press Enter

**Note:** The service also writes to Windows Event Log:
- Open Event Viewer (`eventvwr.msc`)
- Navigate to: Windows Logs → Application
- Look for entries from "LanDesk Service"

## Quick Access Commands

### Open Main App Log Folder
```cmd
explorer %LOCALAPPDATA%\LanDesk\Logs
```

### Open Service Log Folder
```cmd
explorer %PROGRAMDATA%\LanDesk
```

### View Latest Log (PowerShell)
```powershell
Get-Content "$env:LOCALAPPDATA\LanDesk\Logs\LanDesk_$(Get-Date -Format 'yyyyMMdd').log" -Tail 50
```

### View Service Log (PowerShell)
```powershell
Get-Content "$env:PROGRAMDATA\LanDesk\service.log" -Tail 50
```

## Log Levels

- **INFO**: General information about application operation
- **WARN**: Warning messages (non-critical issues)
- **ERROR**: Error messages (exceptions and failures)
- **DEBUG**: Debug information (detailed troubleshooting)

## Troubleshooting

If the app doesn't start:
1. Check the latest log file in `%LOCALAPPDATA%\LanDesk\Logs\`
2. Look for ERROR entries at the end of the file
3. Check Windows Event Viewer for additional errors
4. Verify all required DLL files are present in the installation directory

## Log File Rotation

- Logs are automatically rotated daily
- Old log files are kept (not automatically deleted)
- Each day gets a new log file: `LanDesk_YYYYMMDD.log`
