; LanDesk Installer Script for Inno Setup
; Creates a full installer with firewall configuration

#define AppName "LanDesk"
#define AppVersion "1.0.1"
#define AppPublisher "LanDesk"
#define AppURL "https://github.com/landesk"
#define AppExeName "LanDesk.exe"
#define ServiceExeName "LanDesk.Service.exe"
#define OutputDir "installer"

[Setup]
AppId={{A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=
OutputDir={#OutputDir}
OutputBaseFilename=LanDeskSetup
SetupIconFile=
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; RequestExecutionLevel=admin - Installer requires admin for firewall/service installation
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts"; Flags: unchecked
Name: "startmenu"; Description: "Create Start Menu shortcuts"; GroupDescription: "Additional shortcuts"
Name: "installservice"; Description: "Install as Windows Service (runs in background) - Enables UAC prompt interaction like AnyDesk"; GroupDescription: "Installation options"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"
Source: "..\setup-firewall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\install-service.ps1"; DestDir: "{app}"; Flags: ignoreversion; Check: ServiceFileExists
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Auto-startup: Start LanDesk automatically on Windows startup (minimized to tray)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LanDesk"; ValueData: """{app}\{#AppExeName}"" --minimized"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent; Tasks: not installservice

[Code]
var
  FirewallConfigured: Boolean;

function ServiceFileExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{src}\..\publish\win-x64\LanDesk.Service.exe'));
end;

procedure InitializeWizard;
begin
  FirewallConfigured := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  FirewallScript: String;
  ServiceScript: String;
  Success: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    // Always configure firewall automatically using netsh (most reliable in installers)
    // Installer runs as admin, so these commands will work
    
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Application Inbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Application Outbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Service Inbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Service Outbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Remove existing port-based rules first (ignore errors if they don't exist)
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Discovery"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Screen"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Input"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Server"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Run PowerShell script setup-firewall.ps1
    FirewallScript := ExpandConstant('{app}\setup-firewall.ps1');
    if FileExists(FirewallScript) then
    begin
        Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "& { $env:INSTALLER_RUNNING=''1''; & ''' + FirewallScript + ''' }"', 
             '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
    
    // Install/Update Windows Service if user selected the option
    if IsTaskSelected('installservice') then
    begin
      ServiceScript := ExpandConstant('{app}\install-service.ps1');
      if FileExists(ServiceScript) then
      begin
        // Check if service already exists - if so, stop it first
        Exec('sc.exe', 'query LanDeskService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        if ResultCode = 0 then
        begin
          // Service exists - stop it before installing new version
          Exec('sc.exe', 'stop LanDeskService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
          Sleep(2000); // Wait for service to stop
        end;
        
        // Run service installation script (silently, since installer is already running as admin)
        // This will install or update the service and start it
        Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "& { $env:INSTALLER_RUNNING=''1''; & ''' + ServiceScript + ''' }"', 
             '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end;
    
    FirewallConfigured := True;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Remove application-based firewall rules
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Application Inbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Application Outbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Service Inbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Service Outbound"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Remove old port-based rules (for backward compatibility)
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Discovery"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Screen"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Input"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Discovery Out"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Screen Out"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'advfirewall firewall delete rule name="LanDesk Input Out"', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Uninstall service
    // Uninstall service and remove firewall rules
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup-firewall.ps1') + '" -Remove -Silent', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\install-service.ps1') + '" -Uninstall', 
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
