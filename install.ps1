#Requires -RunAsAdministrator
<#
.SYNOPSIS
    OpenPrint Windows Installation Script

.DESCRIPTION
    Installs OpenPrint as a Windows service for thermal printer management.
    Requires administrator privileges.

.PARAMETER InstallDir
    Installation directory (default: C:\OpenPrint)

.PARAMETER Uninstall
    Uninstall the service instead of installing

.EXAMPLE
    .\install.ps1
    Installs OpenPrint to C:\OpenPrint

.EXAMPLE
    .\install.ps1 -InstallDir "D:\Services\OpenPrint"
    Installs to custom directory

.EXAMPLE
    .\install.ps1 -Uninstall
    Uninstalls the OpenPrint service
#>

param(
    [string]$InstallDir = "C:\OpenPrint",
    [switch]$Uninstall
)

$ServiceName = "OpenPrint"
$ServiceDisplayName = "OpenPrint Thermal Printer Service"
$ServiceDescription = "Proxy server for thermal printers using ESC/POS commands"
$ExeName = "OpenPrint.exe"

function Write-Log {
    param([string]$Message, [string]$Color = "White")
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $Color
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Check administrator privileges
if (-not (Test-Administrator)) {
    Write-Log "This script requires administrator privileges. Please run as Administrator." "Red"
    exit 1
}

# Uninstall mode
if ($Uninstall) {
    Write-Log "Uninstalling OpenPrint service..." "Yellow"

    # Stop service if running
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -eq 'Running') {
            Write-Log "Stopping service..."
            Stop-Service -Name $ServiceName -Force
            Start-Sleep -Seconds 2
        }

        Write-Log "Removing service..."
        sc.exe delete $ServiceName | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Log "Service removed successfully." "Green"
        } else {
            Write-Log "Failed to remove service. Error code: $LASTEXITCODE" "Red"
        }
    } else {
        Write-Log "Service not found." "Yellow"
    }

    # Ask about removing files
    $removeFiles = Read-Host "Remove installation directory $InstallDir? (y/N)"
    if ($removeFiles -eq 'y' -or $removeFiles -eq 'Y') {
        if (Test-Path $InstallDir) {
            Remove-Item -Path $InstallDir -Recurse -Force
            Write-Log "Installation directory removed." "Green"
        }
    }

    Write-Log "Uninstallation complete." "Green"
    exit 0
}

# Installation mode
Write-Log "Installing OpenPrint..." "Cyan"
Write-Log "Installation directory: $InstallDir"

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Log "Service already exists. Stopping for update..." "Yellow"
    if ($existingService.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
}

# Create installation directory
if (-not (Test-Path $InstallDir)) {
    Write-Log "Creating installation directory..."
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

# Get source directory (where this script is located)
$SourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Check if we're running from publish directory or source
$ExePath = Join-Path $SourceDir $ExeName
if (-not (Test-Path $ExePath)) {
    # Try publish subdirectory
    $PublishDir = Join-Path $SourceDir "publish\win-x64"
    $ExePath = Join-Path $PublishDir $ExeName
    if (Test-Path $ExePath) {
        $SourceDir = $PublishDir
    } else {
        Write-Log "OpenPrint.exe not found. Please build the project first:" "Red"
        Write-Log "  dotnet publish -c Release -r win-x64 --self-contained" "Yellow"
        exit 1
    }
}

# Copy files to installation directory
Write-Log "Copying files to $InstallDir..."
$filesToCopy = @(
    "*.exe",
    "*.dll",
    "*.json",
    "*.pdb",
    "wwwroot"
)

foreach ($pattern in $filesToCopy) {
    $items = Get-ChildItem -Path $SourceDir -Filter $pattern -ErrorAction SilentlyContinue
    foreach ($item in $items) {
        if ($item.PSIsContainer) {
            Copy-Item -Path $item.FullName -Destination $InstallDir -Recurse -Force
        } else {
            Copy-Item -Path $item.FullName -Destination $InstallDir -Force
        }
    }
}

# Copy wwwroot if exists
$wwwrootSource = Join-Path $SourceDir "wwwroot"
if (Test-Path $wwwrootSource) {
    $wwwrootDest = Join-Path $InstallDir "wwwroot"
    if (Test-Path $wwwrootDest) {
        Remove-Item -Path $wwwrootDest -Recurse -Force
    }
    Copy-Item -Path $wwwrootSource -Destination $InstallDir -Recurse -Force
}

$InstalledExePath = Join-Path $InstallDir $ExeName

# Create or update service
if ($existingService) {
    Write-Log "Updating existing service..."
    sc.exe config $ServiceName binPath= "`"$InstalledExePath`"" | Out-Null
} else {
    Write-Log "Creating Windows service..."
    sc.exe create $ServiceName `
        binPath= "`"$InstalledExePath`"" `
        start= auto `
        DisplayName= "$ServiceDisplayName" | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Failed to create service. Error code: $LASTEXITCODE" "Red"
        exit 1
    }
}

# Set service description
sc.exe description $ServiceName "$ServiceDescription" | Out-Null

# Configure service recovery options (restart on failure)
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

# Start the service
Write-Log "Starting service..."
Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Verify service status
$service = Get-Service -Name $ServiceName
if ($service.Status -eq 'Running') {
    Write-Log "Service started successfully!" "Green"
} else {
    Write-Log "Service may not have started. Status: $($service.Status)" "Yellow"
    Write-Log "Check Windows Event Viewer for details." "Yellow"
}

# Display information
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenPrint Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Installation directory: $InstallDir"
Write-Host "Service name: $ServiceName"
Write-Host "Service status: $($service.Status)"
Write-Host ""
Write-Host "Web interface: http://127.0.0.1:5050/" -ForegroundColor Yellow
Write-Host ""
Write-Host "Useful commands:" -ForegroundColor Cyan
Write-Host "  Start service:   Start-Service $ServiceName"
Write-Host "  Stop service:    Stop-Service $ServiceName"
Write-Host "  Restart service: Restart-Service $ServiceName"
Write-Host "  Check status:    Get-Service $ServiceName"
Write-Host "  View logs:       Get-EventLog -LogName Application -Source $ServiceName"
Write-Host ""
Write-Host "Configuration file: $InstallDir\appsettings.json"
Write-Host ""

# Open web interface
$openBrowser = Read-Host "Open web interface in browser? (Y/n)"
if ($openBrowser -ne 'n' -and $openBrowser -ne 'N') {
    Start-Process "http://127.0.0.1:5050/"
}
