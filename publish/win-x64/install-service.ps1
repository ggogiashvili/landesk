# LanDesk Service Installer Script
# Installs LanDesk as a Windows Service

param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "LanDesk Service Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run PowerShell as Administrator." -ForegroundColor Yellow
    # Only pause if not running from installer (check if output is visible)
    if ($Host.UI.RawUI.WindowSize.Width -gt 0) {
        pause
    }
    exit 1
}

$serviceName = "LanDesk Remote Desktop Service"
$serviceExe = "LanDesk.Service.exe"

# Determine service path - check if running from installer (app directory) or build directory
if (Test-Path (Join-Path $PSScriptRoot $serviceExe)) {
    # Running from installed location
    $servicePath = Join-Path $PSScriptRoot $serviceExe
} elseif (Test-Path (Join-Path $PSScriptRoot "publish\win-x64\$serviceExe")) {
    # Running from build directory
    $servicePath = Join-Path $PSScriptRoot "publish\win-x64\$serviceExe"
} else {
    # Try current directory
    $servicePath = Join-Path (Get-Location) $serviceExe
}

if ($Uninstall) {
    Write-Host "Uninstalling LanDesk Service..." -ForegroundColor Yellow
    
    try {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service) {
            if ($service.Status -eq 'Running') {
                Stop-Service -Name $serviceName -Force
                Write-Host "Service stopped." -ForegroundColor Green
            }
            sc.exe delete $serviceName
            Write-Host "Service uninstalled successfully." -ForegroundColor Green
        } else {
            Write-Host "Service not found." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "Error uninstalling service: $_" -ForegroundColor Red
    }
}
else {
    if (-not (Test-Path $servicePath)) {
        Write-Host "ERROR: Service executable not found at: $servicePath" -ForegroundColor Red
        Write-Host "Please build the service first using publish.bat" -ForegroundColor Yellow
        pause
        exit 1
    }

    Write-Host "Installing LanDesk Service..." -ForegroundColor Yellow
    Write-Host "Service path: $servicePath" -ForegroundColor Gray
    Write-Host ""

    try {
        # Check if service already exists
        $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($existingService) {
            Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow
            if ($existingService.Status -eq 'Running') {
                Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            }
            sc.exe delete $serviceName | Out-Null
            Start-Sleep -Seconds 2
        }

        # Install the service
        Write-Host "Creating Windows Service..." -ForegroundColor Yellow
        # Create service - it will run as LocalSystem by default (SYSTEM privileges)
        # This is required for UAC/secure desktop access (like AnyDesk)
        $result = sc.exe create $serviceName binPath= "`"$servicePath`"" start= auto DisplayName= "LanDesk Remote Desktop Service" 2>&1 | Out-String
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Service created successfully." -ForegroundColor Green
            
            # Set service description
            sc.exe description $serviceName "Provides remote desktop functionality for LanDesk on the local network. Runs as SYSTEM to enable UAC prompt interaction." | Out-Null
            
            # Configure service to run as LocalSystem (SYSTEM account) - required for secure desktop access
            # This allows the service to interact with UAC prompts (Winlogon desktop)
            sc.exe config $serviceName obj= "LocalSystem" 2>&1 | Out-Null
            
            # Allow service to interact with desktop (for UAC support)
            # Note: This may not work on all Windows versions, but service can still access secure desktop via API
            sc.exe config $serviceName type= interact 2>&1 | Out-Null
            
            # Start the service
            Write-Host "Starting service..." -ForegroundColor Yellow
            Start-Service -Name $serviceName -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($service -and $service.Status -eq 'Running') {
                Write-Host "Service started successfully!" -ForegroundColor Green
            } else {
                Write-Host "Warning: Service installed but not running. Status: $($service.Status)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "Failed to create service. Error code: $LASTEXITCODE" -ForegroundColor Red
            Write-Host "Output: $result" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "Error installing service: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Done!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

# Only pause if running interactively (not from installer)
# Check if running from installer by checking if window is hidden or environment variable is set
$isInstaller = $env:INSTALLER_RUNNING -eq '1' -or $Host.UI.RawUI.WindowSize.Width -eq 0
if (-not $isInstaller) {
    pause
}
