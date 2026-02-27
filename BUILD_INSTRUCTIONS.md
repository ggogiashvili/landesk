# LanDesk Build and Installation Instructions

## Quick Build to Executable

### Method 1: PowerShell Script (Recommended)

```powershell
.\publish.ps1
```

This creates a self-contained executable in `publish\win-x64\LanDesk.exe` that can run on any Windows 10/11 machine without requiring .NET to be installed.

### Method 2: Batch Script

```cmd
publish.bat
```

### Method 3: Manual Command

```bash
dotnet publish LanDesk\LanDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o publish\win-x64
```

## What You Get

After publishing, you'll have:
- **`publish\win-x64\LanDesk.exe`** - Single executable file (~70-100 MB)
- This file is **self-contained** - includes everything needed to run
- No installation required - just run the .exe file
- Works on Windows 10/11 (64-bit)

## Distribution

### Option 1: Direct Distribution
Simply share the `LanDesk.exe` file. Users can run it directly.

### Option 2: ZIP Archive
1. Create a ZIP file containing `LanDesk.exe`
2. Share the ZIP file
3. Users extract and run

### Option 3: Create Installer
See `INSTALLER_GUIDE.md` for detailed instructions on creating:
- MSI installer (WiX)
- Inno Setup installer
- NSIS installer
- ClickOnce deployment

## Firewall Configuration

LanDesk requires Windows Firewall rules for:
- **UDP Port 54987** (Discovery)
- **TCP Port 54988** (Control)

### Automatic Setup (PowerShell as Administrator)

```powershell
.\setup-firewall.ps1
```

### Manual Setup

1. Open Windows Firewall
2. Click "Advanced settings"
3. Create Inbound Rules:
   - **Rule 1**: Allow UDP port 54987 (Discovery)
   - **Rule 2**: Allow TCP port 54988 (Control)

Or use command line (as Administrator):
```cmd
netsh advfirewall firewall add rule name="LanDesk Discovery" dir=in action=allow protocol=UDP localport=54987
netsh advfirewall firewall add rule name="LanDesk Control" dir=in action=allow protocol=TCP localport=54988
```

## Testing the Executable

1. Copy `LanDesk.exe` to a test machine (or same machine)
2. Run the executable
3. Click "Start Discovery"
4. Verify it discovers other devices on the network

## File Size

- **Self-contained**: ~70-100 MB (includes .NET runtime)
- **Framework-dependent**: ~5-10 MB (requires .NET 8.0 installed)

For distribution, self-contained is recommended unless you can guarantee .NET is installed.

## Troubleshooting

### "Application requires .NET runtime"
- Use `--self-contained true` when publishing
- This includes the .NET runtime in the executable

### "Windows protected your PC" warning
- This is normal for unsigned executables
- Click "More info" → "Run anyway"
- To avoid: Code sign your executable (see INSTALLER_GUIDE.md)

### Firewall blocking connections
- Run `setup-firewall.ps1` as Administrator
- Or manually configure Windows Firewall

### Antivirus flags the executable
- This can happen with unsigned executables
- Add exception in antivirus software
- Consider code signing for production

## Next Steps

1. **Build**: Run `publish.ps1`
2. **Test**: Run the executable on target machines
3. **Distribute**: Share the .exe file or create an installer
4. **Configure Firewall**: Set up firewall rules on target machines

For advanced installation options, see `INSTALLER_GUIDE.md`.
