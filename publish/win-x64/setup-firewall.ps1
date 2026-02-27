# LanDesk Firewall Configuration Script
# Configures Windows Firewall rules for LanDesk

param(
    [switch]$Remove,
    [switch]$Silent
)

$ErrorActionPreference = "Continue"

# Check if running from installer (silent mode)
$isInstaller = $env:INSTALLER_RUNNING -eq '1' -or $Silent -or $Host.UI.RawUI.WindowSize.Width -eq 0

if (-not $isInstaller) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "LanDesk Firewall Configuration" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

# Check for administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    if (-not $isInstaller) {
        Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
        Write-Host ""
        Write-Host "To run this script:" -ForegroundColor Yellow
        Write-Host "1. Right-click PowerShell" -ForegroundColor White
        Write-Host "2. Select 'Run as Administrator'" -ForegroundColor White
        Write-Host "3. Navigate to this directory" -ForegroundColor White
        Write-Host "4. Run: .\setup-firewall.ps1" -ForegroundColor White
        Write-Host ""
        Write-Host "Or use this command:" -ForegroundColor Yellow
        $scriptPath = Join-Path $PSScriptRoot "setup-firewall.ps1"
        Write-Host "  Start-Process powershell -Verb RunAs" -ForegroundColor White
        Write-Host "    -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File" -ForegroundColor White
        Write-Host "    $scriptPath" -ForegroundColor White
        Write-Host ""
        pause
        exit 1
    }
    # If called from installer but not admin, try to continue anyway (installer should be admin)
    # This handles edge cases where the check might fail even though we have admin rights
}

if (-not $isInstaller) {
    Write-Host "Running with administrator privileges..." -ForegroundColor Green
    Write-Host ""
}

if ($Remove) {
    Write-Host "Removing LanDesk firewall rules..." -ForegroundColor Yellow
    
    try {
        # Remove inbound rules
        Remove-NetFirewallRule -DisplayName "LanDesk Discovery" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "LanDesk Screen" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "LanDesk Input" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "LanDesk Approval" -ErrorAction SilentlyContinue
        # Remove outbound rules
        Remove-NetFirewallRule -DisplayName "LanDesk Discovery Out" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "LanDesk Screen Out" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "LanDesk Input Out" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "LanDesk Approval Out" -ErrorAction SilentlyContinue
        Write-Host "Firewall rules removed successfully." -ForegroundColor Green
    }
    catch {
        Write-Host "Error removing firewall rules: $_" -ForegroundColor Red
    }
}
else {
    Write-Host "Checking and adding LanDesk firewall rules..." -ForegroundColor Yellow
    
    try {
        # Check if rules already exist
        $existingDiscovery = Get-NetFirewallRule -DisplayName "LanDesk Discovery" -ErrorAction SilentlyContinue
        $existingScreen = Get-NetFirewallRule -DisplayName "LanDesk Screen" -ErrorAction SilentlyContinue
        $existingInput = Get-NetFirewallRule -DisplayName "LanDesk Input" -ErrorAction SilentlyContinue
        $existingApproval = Get-NetFirewallRule -DisplayName "LanDesk Approval" -ErrorAction SilentlyContinue
        $existingUDP = Get-NetFirewallRule -DisplayName "LanDesk Control" -ErrorAction SilentlyContinue

        if ($existingDiscovery) {
            Write-Host "Discovery rule (UDP 25536) already exists, skipping..." -ForegroundColor Green
        }
        
        if ($existingScreen) {
            Write-Host "Screen rule (TCP 8530) already exists, skipping..." -ForegroundColor Green
        }
        
        if ($existingInput) {
            Write-Host "Input rule (TCP 8531) already exists, skipping..." -ForegroundColor Green
        }
        
        if ($existingApproval) {
            Write-Host "Approval rule (TCP 10123) already exists, skipping..." -ForegroundColor Green
        }
        
        # Remove old rule names if they exist (legacy ports)
        if ($existingUDP) {
            Write-Host "Removing old firewall rule..." -ForegroundColor Yellow
            Remove-NetFirewallRule -DisplayName "LanDesk Control" -ErrorAction SilentlyContinue
        }
        
        # Discovery port (UDP 25536) - only add if it doesn't exist
        if (-not $existingDiscovery) {
            Write-Host "Adding UDP rule (port 25536 - Discovery)..." -ForegroundColor Yellow
            $udpRule = New-NetFirewallRule -DisplayName "LanDesk Discovery" `
                -Direction Inbound `
                -Protocol UDP `
                -LocalPort 25536 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk device discovery on local network" `
                -ErrorAction Stop
            
            if ($udpRule) {
                Write-Host "  UDP rule added successfully" -ForegroundColor Green
            }
        }
        
        # Screen streaming port (TCP 8530) - only add if it doesn't exist
        if (-not $existingScreen) {
            Write-Host "Adding TCP rule (port 8530 - Screen)..." -ForegroundColor Yellow
            $screenRule = New-NetFirewallRule -DisplayName "LanDesk Screen" `
                -Direction Inbound `
                -Protocol TCP `
                -LocalPort 8530 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk screen streaming" `
                -ErrorAction Stop
            
            if ($screenRule) {
                Write-Host "  Screen streaming rule added successfully" -ForegroundColor Green
            }
        }

        # Input control port (TCP 8531) - only add if it doesn't exist
        if (-not $existingInput) {
            Write-Host "Adding TCP rule (port 8531 - Input)..." -ForegroundColor Yellow
            $inputRule = New-NetFirewallRule -DisplayName "LanDesk Input" `
                -Direction Inbound `
                -Protocol TCP `
                -LocalPort 8531 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk remote control input" `
                -ErrorAction Stop
            
            if ($inputRule) {
                Write-Host "  Input control rule added successfully" -ForegroundColor Green
            }
        }

        # Approval port (TCP 10123, localhost) - only add if it doesn't exist
        if (-not $existingApproval) {
            Write-Host "Adding TCP rule (port 10123 - Approval)..." -ForegroundColor Yellow
            $approvalRule = New-NetFirewallRule -DisplayName "LanDesk Approval" `
                -Direction Inbound `
                -Protocol TCP `
                -LocalPort 10123 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk connection approval (localhost)" `
                -ErrorAction Stop
            
            if ($approvalRule) {
                Write-Host "  Approval rule added successfully" -ForegroundColor Green
            }
        }
        
        # Add OUTBOUND rules (for connections initiated by this device)
        Write-Host "Adding outbound firewall rules..." -ForegroundColor Yellow
        
        # Check if outbound rules exist
        $existingDiscoveryOut = Get-NetFirewallRule -DisplayName "LanDesk Discovery Out" -ErrorAction SilentlyContinue
        $existingScreenOut = Get-NetFirewallRule -DisplayName "LanDesk Screen Out" -ErrorAction SilentlyContinue
        $existingInputOut = Get-NetFirewallRule -DisplayName "LanDesk Input Out" -ErrorAction SilentlyContinue
        $existingApprovalOut = Get-NetFirewallRule -DisplayName "LanDesk Approval Out" -ErrorAction SilentlyContinue
        
        # UDP 25536 outbound for discovery
        if (-not $existingDiscoveryOut) {
            $udpRuleOut = New-NetFirewallRule -DisplayName "LanDesk Discovery Out" `
                -Direction Outbound `
                -Protocol UDP `
                -LocalPort 25536 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk device discovery outbound" `
                -ErrorAction Stop
            if ($udpRuleOut) {
                Write-Host "  UDP outbound rule added successfully" -ForegroundColor Green
            }
        }
        
        # TCP 8530 outbound for screen streaming
        if (-not $existingScreenOut) {
            $screenRuleOut = New-NetFirewallRule -DisplayName "LanDesk Screen Out" `
                -Direction Outbound `
                -Protocol TCP `
                -LocalPort 8530 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk screen streaming outbound" `
                -ErrorAction Stop
            if ($screenRuleOut) {
                Write-Host "  Screen outbound rule added successfully" -ForegroundColor Green
            }
        }
        
        # TCP 8531 outbound for input control
        if (-not $existingInputOut) {
            $inputRuleOut = New-NetFirewallRule -DisplayName "LanDesk Input Out" `
                -Direction Outbound `
                -Protocol TCP `
                -LocalPort 8531 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk remote control input outbound" `
                -ErrorAction Stop
            if ($inputRuleOut) {
                Write-Host "  Input outbound rule added successfully" -ForegroundColor Green
            }
        }
        
        # TCP 10123 outbound for approval
        if (-not $existingApprovalOut) {
            $approvalRuleOut = New-NetFirewallRule -DisplayName "LanDesk Approval Out" `
                -Direction Outbound `
                -Protocol TCP `
                -LocalPort 10123 `
                -Action Allow `
                -Profile Domain,Private,Public `
                -Description "Allows LanDesk approval outbound" `
                -ErrorAction Stop
            if ($approvalRuleOut) {
                Write-Host "  Approval outbound rule added successfully" -ForegroundColor Green
            }
        }
        
        Write-Host ""
        Write-Host "Firewall rules added successfully:" -ForegroundColor Green
        Write-Host "  Inbound:" -ForegroundColor White
        Write-Host "    - UDP 25536 (Discovery)" -ForegroundColor White
        Write-Host "    - TCP 8530 (Screen Streaming)" -ForegroundColor White
        Write-Host "    - TCP 8531 (Remote Control)" -ForegroundColor White
        Write-Host "    - TCP 10123 (Approval)" -ForegroundColor White
        Write-Host "  Outbound:" -ForegroundColor White
        Write-Host "    - UDP 25536 (Discovery)" -ForegroundColor White
        Write-Host "    - TCP 8530 (Screen Streaming)" -ForegroundColor White
        Write-Host "    - TCP 8531 (Remote Control)" -ForegroundColor White
        Write-Host "    - TCP 10123 (Approval)" -ForegroundColor White
        
        # Return success exit code
        if ($isInstaller) {
            exit 0
        }
    }
    catch {
        Write-Host ""
        Write-Host "ERROR: Failed to add firewall rules" -ForegroundColor Red
        Write-Host "Error details: $_" -ForegroundColor Red
        Write-Host ""
        if (-not $isInstaller) {
            Write-Host "You may need to configure Windows Firewall manually:" -ForegroundColor Yellow
            Write-Host "1. Open Windows Defender Firewall" -ForegroundColor White
            Write-Host "2. Click 'Advanced settings'" -ForegroundColor White
            Write-Host "3. Create Inbound Rules for:" -ForegroundColor White
            Write-Host "   - UDP port 25536" -ForegroundColor White
            Write-Host "   - TCP ports 8530, 8531, 10123" -ForegroundColor White
            Write-Host ""
            pause
        }
        exit 1
    }
}

Write-Host ""
Write-Host "Verifying firewall rules..." -ForegroundColor Yellow
$rules = Get-NetFirewallRule -DisplayName "LanDesk*" -ErrorAction SilentlyContinue
if ($rules) {
    Write-Host "Found firewall rules:" -ForegroundColor Green
    $rules | ForEach-Object {
        $enabled = if ($_.Enabled) { "Enabled" } else { "Disabled" }
        Write-Host "  - $($_.DisplayName): $enabled" -ForegroundColor White
    }
} else {
    Write-Host "Warning: Could not verify firewall rules" -ForegroundColor Yellow
}

if (-not $isInstaller) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Configuration Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "To verify manually, run:" -ForegroundColor Gray
    Write-Host "  Get-NetFirewallRule -DisplayName LanDesk*" -ForegroundColor White
    Write-Host ""
}
