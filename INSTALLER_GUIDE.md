# LanDesk Installation Guide

This guide explains how to create installable executables and installers for LanDesk.

## Quick Start: Self-Contained Executable

The easiest way to distribute LanDesk is as a self-contained executable that includes everything needed to run.

### Option 1: Using PowerShell Script (Recommended)

```powershell
.\publish.ps1
```

This creates:
- `publish\win-x64\LanDesk.exe` - 64-bit executable
- `publish\win-x86\LanDesk.exe` - 32-bit executable

### Option 2: Using Batch Script

```cmd
publish.bat
```

### Option 3: Manual Publishing

```bash
# 64-bit version
dotnet publish LanDesk\LanDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o publish\win-x64

# 32-bit version
dotnet publish LanDesk\LanDesk.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o publish\win-x86
```

## Distribution

The published executables are **self-contained** - they include the .NET runtime and all dependencies. Users can run them directly without installing .NET.

### File Sizes
- 64-bit: ~70-100 MB
- 32-bit: ~60-80 MB

### Distribution Methods

1. **Direct Distribution**: Share the `.exe` file directly
2. **ZIP Archive**: Compress and distribute as `.zip`
3. **Installer**: Create a proper Windows installer (see below)

## Creating a Windows Installer

### Option 1: WiX Toolset (Professional MSI Installer)

WiX (Windows Installer XML) creates professional MSI installers.

#### Prerequisites
1. Install WiX Toolset: https://wixtoolset.org/
2. Install WiX Visual Studio Extension (optional)

#### Create WiX Project

1. Create a new WiX project in Visual Studio or use the provided template:

```xml
<!-- Installer.wxs -->
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" Name="LanDesk" Language="1033" Version="1.0.0" 
           Manufacturer="LanDesk" UpgradeCode="YOUR-GUID-HERE">
    <Package InstallerVersion="200" Compressed="yes" InstallScope="perMachine" />
    
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
    <MediaTemplate />
    
    <Feature Id="ProductFeature" Title="LanDesk" Level="1">
      <ComponentGroupRef Id="ProductComponents" />
    </Feature>
  </Product>
  
  <Fragment>
    <Directory Id="TARGETDIR" Name="SourceDir">
      <Directory Id="ProgramFilesFolder">
        <Directory Id="INSTALLFOLDER" Name="LanDesk" />
      </Directory>
      <Directory Id="ProgramMenuFolder">
        <Directory Id="ApplicationProgramsFolder" Name="LanDesk" />
      </Directory>
    </Directory>
  </Fragment>
  
  <Fragment>
    <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">
      <Component Id="LanDeskExe">
        <File Id="LanDeskExe" Source="$(var.LanDesk.TargetPath)" KeyPath="yes" />
        <Shortcut Id="StartMenuShortcut" Directory="ApplicationProgramsFolder" 
                  Name="LanDesk" WorkingDirectory="INSTALLFOLDER" 
                  Icon="icon.ico" />
      </Component>
    </ComponentGroup>
  </Fragment>
</Wix>
```

### Option 2: Inno Setup (Simple Installer)

Inno Setup is a free installer that's easier to use than WiX.

#### Prerequisites
1. Download Inno Setup: https://jrsoftware.org/isinfo.php

#### Create Inno Setup Script

```ini
; LanDesk.iss
[Setup]
AppName=LanDesk
AppVersion=1.0.0
AppPublisher=LanDesk
AppPublisherURL=https://github.com/yourusername/landesk
DefaultDirName={pf}\LanDesk
DefaultGroupName=LanDesk
OutputDir=installer
OutputBaseFilename=LanDeskSetup
Compression=lzma
SolidCompression=yes
SetupIconFile=icon.ico
PrivilegesRequired=admin

[Files]
Source: "publish\win-x64\LanDesk.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\LanDesk"; Filename: "{app}\LanDesk.exe"
Name: "{commondesktop}\LanDesk"; Filename: "{app}\LanDesk.exe"

[Run]
Filename: "{app}\LanDesk.exe"; Description: "Launch LanDesk"; Flags: nowait postinstall skipifsilent
```

### Option 3: NSIS (Nullsoft Scriptable Install System)

NSIS is another popular free installer.

#### Prerequisites
1. Download NSIS: https://nsis.sourceforge.io/

#### Create NSIS Script

```nsis
; LanDesk.nsi
!define APP_NAME "LanDesk"
!define APP_VERSION "1.0.0"
!define APP_PUBLISHER "LanDesk"
!define APP_EXE "LanDesk.exe"

Name "${APP_NAME}"
OutFile "LanDeskSetup.exe"
InstallDir "$PROGRAMFILES\LanDesk"
RequestExecutionLevel admin

Page directory
Page instfiles

Section "Install"
    SetOutPath "$INSTDIR"
    File "publish\win-x64\${APP_EXE}"
    
    CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    
    WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\Uninstall.exe"
    Delete "$SMPROGRAMS\${APP_NAME}.lnk"
    Delete "$DESKTOP\${APP_NAME}.lnk"
    RMDir "$INSTDIR"
SectionEnd
```

## ClickOnce Deployment (Alternative)

ClickOnce is built into Visual Studio and provides simple deployment.

### Steps

1. Right-click the `LanDesk` project in Visual Studio
2. Select "Publish"
3. Choose "ClickOnce" as the publish method
4. Configure settings:
   - Publishing location: Local folder or network share
   - Installation mode: "The application is available offline"
5. Click "Publish"

### Advantages
- Automatic updates
- Simple installation for users
- No admin rights required (user-level install)

### Disadvantages
- Requires internet/network share for updates
- Less control over installation process

## Code Signing (Recommended for Distribution)

To avoid Windows security warnings, sign your executable:

### Using SignTool

```bash
signtool sign /f certificate.pfx /p password /t http://timestamp.digicert.com LanDesk.exe
```

### Using Visual Studio
1. Project Properties → Signing
2. Check "Sign the ClickOnce manifests" or "Sign the assembly"
3. Select certificate

## Firewall Configuration

The installer should configure Windows Firewall rules automatically. Add this to your installer:

### PowerShell Script for Firewall

```powershell
# Allow UDP port 54987 (Discovery)
New-NetFirewallRule -DisplayName "LanDesk Discovery" -Direction Inbound -Protocol UDP -LocalPort 54987 -Action Allow

# Allow TCP port 54988 (Control)
New-NetFirewallRule -DisplayName "LanDesk Control" -Direction Inbound -Protocol TCP -LocalPort 54988 -Action Allow
```

## Testing the Installer

1. **Test on Clean Machine**: Install on a machine without .NET installed
2. **Test Firewall**: Verify firewall rules are created
3. **Test Uninstall**: Ensure clean uninstallation
4. **Test Updates**: Verify upgrade scenarios work

## Distribution Checklist

- [ ] Build self-contained executable
- [ ] Test on clean Windows machine
- [ ] Create installer (MSI/Inno/NSIS)
- [ ] Configure firewall rules
- [ ] Code sign executable (optional but recommended)
- [ ] Create installation documentation
- [ ] Test uninstallation
- [ ] Package for distribution

## File Structure After Publishing

```
publish/
├── win-x64/
│   └── LanDesk.exe          (Self-contained, ~70-100 MB)
└── win-x86/
    └── LanDesk.exe          (Self-contained, ~60-80 MB)
```

## Quick Commands Reference

```bash
# Publish 64-bit only
dotnet publish LanDesk\LanDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# Publish with compression
dotnet publish LanDesk\LanDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish

# Publish framework-dependent (smaller, requires .NET)
dotnet publish LanDesk\LanDesk.csproj -c Release -r win-x64 -o publish
```

## Troubleshooting

### "Application requires .NET runtime"
- Use `--self-contained true` flag
- Ensure `PublishSingleFile=true` is set

### Large file size
- This is normal for self-contained apps (~70-100 MB)
- Consider framework-dependent deployment if .NET is guaranteed

### Antivirus warnings
- Code sign your executable
- Submit to antivirus vendors for whitelisting

### Firewall blocking
- Add firewall rules during installation
- Provide manual firewall configuration instructions
