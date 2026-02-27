# LanDesk Quick Start Guide

## Build to Executable (5 minutes)

### Step 1: Build the Executable

Open PowerShell in the project directory and run:

```powershell
.\publish.ps1
```

Or use the batch file:

```cmd
publish.bat
```

### Step 2: Find Your Executable

After building, you'll find:
- **`publish\win-x64\LanDesk.exe`** - Ready to use!

### Step 3: Configure Firewall (Required)

Run PowerShell **as Administrator**:

```powershell
.\setup-firewall.ps1
```

Or manually:
1. Windows Firewall → Advanced Settings
2. Add Inbound Rules:
   - UDP Port 54987 (Discovery)
   - TCP Port 54988 (Control)

### Step 4: Run and Test

1. Run `LanDesk.exe` on multiple machines on the same network
2. Click "Start Discovery" on each machine
3. Devices should appear in the list
4. Select a device and click "Connect"

## That's It!

The executable is self-contained - no installation needed. Just run it!

## Need an Installer?

See `INSTALLER_GUIDE.md` for creating professional installers.

## Troubleshooting

**Devices not appearing?**
- Check firewall rules are configured
- Ensure all machines are on the same LAN
- Try clicking "Refresh"

**Connection failed?**
- Verify firewall allows ports 54987 (UDP) and 54988 (TCP)
- Check both machines are running LanDesk

**Windows security warning?**
- Normal for unsigned executables
- Click "More info" → "Run anyway"
- Code sign for production (see INSTALLER_GUIDE.md)
