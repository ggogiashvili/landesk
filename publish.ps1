# LanDesk Publishing Script
# Creates a self-contained executable for Windows

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\publish"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "LanDesk Publishing Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $OutputPath) {
    try {
        Remove-Item -Path $OutputPath -Recurse -Force -ErrorAction Stop
        Start-Sleep -Seconds 2  # Give Windows time to release file handles
    }
    catch {
        Write-Host "Warning: Some files may be in use. Continuing anyway..." -ForegroundColor Yellow
        # Try to remove individual files
        Get-ChildItem -Path $OutputPath -Recurse -File | ForEach-Object {
            try {
                Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
            } catch { }
        }
        Start-Sleep -Seconds 1
    }
}

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to restore packages!" -ForegroundColor Red
    exit 1
}

# Build solution
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Clean first to ensure fresh build
Write-Host "Cleaning project..." -ForegroundColor Yellow
dotnet clean LanDesk\LanDesk.csproj -c $Configuration | Out-Null

# Publish application (without single-file to avoid bundle manifest issues)
Write-Host "Publishing application..." -ForegroundColor Yellow
dotnet publish LanDesk\LanDesk.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:CopyOutputSymbolsToPublishDirectory=false `
    --no-incremental `
    -o "$OutputPath\win-x64"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Publishing Windows Service..." -ForegroundColor Yellow
    Start-Sleep -Milliseconds 500  # Small delay to avoid conflicts
    dotnet publish LanDesk.Service\LanDesk.Service.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:PublishTrimmed=false `
        -o "$OutputPath\win-x64-service"
    
    # Copy service files to main publish directory (but exclude WPF DLLs to avoid overwriting)
    if (Test-Path "$OutputPath\win-x64-service\LanDesk.Service.exe") {
        $excludePatterns = @(
            "WindowsBase.dll",
            "PresentationCore.dll",
            "PresentationFramework*.dll",
            "System.Windows*.dll",
            "System.Xaml.dll",
            "UIAutomation*.dll"
        )
        
        Get-ChildItem -Path "$OutputPath\win-x64-service" -File | ForEach-Object {
            $shouldExclude = $false
            foreach ($pattern in $excludePatterns) {
                if ($_.Name -like $pattern) {
                    $shouldExclude = $true
                    break
                }
            }
            if (-not $shouldExclude) {
                Copy-Item $_.FullName -Destination "$OutputPath\win-x64\" -Force
            }
        }
        
        # Copy subdirectories (like runtimes) if they exist
        Get-ChildItem -Path "$OutputPath\win-x64-service" -Directory | ForEach-Object {
            Copy-Item $_.FullName -Destination "$OutputPath\win-x64\" -Recurse -Force
        }
    }
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Also publish for win-x86 (32-bit)
Write-Host "Publishing 32-bit version..." -ForegroundColor Yellow
dotnet publish LanDesk\LanDesk.csproj `
    -c $Configuration `
    -r win-x86 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$OutputPath\win-x86"

Write-Host "Publishing 32-bit service..." -ForegroundColor Yellow
dotnet publish LanDesk.Service\LanDesk.Service.csproj `
    -c $Configuration `
    -r win-x86 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$OutputPath\win-x86"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Publishing Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output location: $OutputPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files created:" -ForegroundColor Yellow
Write-Host "  - $OutputPath\win-x64\LanDesk.exe (64-bit)" -ForegroundColor White
Write-Host "  - $OutputPath\win-x64\LanDesk.Service.exe (64-bit Service)" -ForegroundColor White
Write-Host "  - $OutputPath\win-x86\LanDesk.exe (32-bit)" -ForegroundColor White
Write-Host ""
Write-Host "These are self-contained applications that can run without .NET installed." -ForegroundColor Gray
Write-Host "Note: DLL files are included alongside the executables (required for WPF applications)." -ForegroundColor Gray
Write-Host ""
