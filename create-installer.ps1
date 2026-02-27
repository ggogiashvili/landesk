# LanDesk Installer Creation Script
# Creates Inno Setup installer with all components

param(
    [string]$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "LanDesk Installer Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Inno Setup is installed
if (-not (Test-Path $InnoSetupPath)) {
    Write-Host "Inno Setup not found at: $InnoSetupPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please:" -ForegroundColor Yellow
    Write-Host "1. Download and install Inno Setup from: https://jrsoftware.org/isinfo.php" -ForegroundColor White
    Write-Host "2. Or provide the path to ISCC.exe:" -ForegroundColor White
    Write-Host "   .\create-installer.ps1 -InnoSetupPath 'C:\Path\To\ISCC.exe'" -ForegroundColor White
    Write-Host ""
    
    # Try to find Inno Setup automatically
    $possiblePaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $InnoSetupPath = $path
            Write-Host "Found Inno Setup at: $path" -ForegroundColor Green
            break
        }
    }
    
    if (-not (Test-Path $InnoSetupPath)) {
        Write-Host "ERROR: Could not find Inno Setup. Please install it first." -ForegroundColor Red
        exit 1
    }
}

# Always rebuild to ensure latest code is included
Write-Host "Rebuilding application and service..." -ForegroundColor Yellow
Write-Host ""

# Run publish script to rebuild everything
if (Test-Path "publish.bat") {
    & cmd /c "publish.bat"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed!" -ForegroundColor Red
        exit 1
    }
} elseif (Test-Path "publish.ps1") {
    .\publish.ps1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed!" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "ERROR: publish.bat or publish.ps1 not found." -ForegroundColor Red
    exit 1
}

# Verify build succeeded
if (-not (Test-Path "publish\win-x64\LanDesk.exe")) {
    Write-Host "ERROR: Build completed but LanDesk.exe not found in publish\win-x64\" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "publish\win-x64\LanDesk.Service.exe")) {
    Write-Host "WARNING: LanDesk.Service.exe not found. Service installation will be skipped." -ForegroundColor Yellow
}

# Copy installer scripts to publish directory
Write-Host "Preparing installer files..." -ForegroundColor Yellow

$installerDir = "installer"
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

# Copy firewall script
Copy-Item "setup-firewall.ps1" -Destination "publish\win-x64\" -Force -ErrorAction SilentlyContinue

# Copy service installer script
Copy-Item "install-service.ps1" -Destination "publish\win-x64\" -Force -ErrorAction SilentlyContinue

# Compile installer
Write-Host ""
Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Yellow
Write-Host ""

$issFile = Join-Path $PSScriptRoot "installer\LanDesk.iss"
if (-not (Test-Path $issFile)) {
    Write-Host "ERROR: Installer script not found: $issFile" -ForegroundColor Red
    exit 1
}

# Change to installer directory so relative paths work
Push-Location (Join-Path $PSScriptRoot "installer")
$result = & $InnoSetupPath "LanDesk.iss"
Pop-Location

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Installer Created Successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installer location: installer\LanDeskSetup.exe" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "The installer will:" -ForegroundColor Yellow
    Write-Host "  - Install LanDesk application" -ForegroundColor White
    Write-Host "  - Optionally install as Windows Service" -ForegroundColor White
    Write-Host "  - Configure Windows Firewall automatically" -ForegroundColor White
    Write-Host "  - Create Start Menu shortcuts" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "ERROR: Installer compilation failed!" -ForegroundColor Red
    exit 1
}
