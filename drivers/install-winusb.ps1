#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs WinUSB driver for USB thermal printers

.DESCRIPTION
    This script helps install the WinUSB driver for USB printers,
    allowing OpenPrint to communicate directly without manufacturer drivers.

    Uses Zadig CLI or libwdi for driver installation.

.PARAMETER VendorId
    USB Vendor ID in hex (e.g., "0483" or "1A86")

.PARAMETER ProductId
    USB Product ID in hex (e.g., "5720" or "7523")

.PARAMETER ListDevices
    List all connected USB devices

.EXAMPLE
    .\install-winusb.ps1 -ListDevices
    Lists all USB devices

.EXAMPLE
    .\install-winusb.ps1 -VendorId "0483" -ProductId "5720"
    Installs WinUSB driver for device with VID:0483 PID:5720
#>

param(
    [string]$VendorId,
    [string]$ProductId,
    [switch]$ListDevices,
    [switch]$Auto
)

$ErrorActionPreference = "Stop"

function Write-Log {
    param([string]$Message, [string]$Color = "White")
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $Color
}

function Get-UsbDevices {
    Write-Log "Scanning USB devices..." "Cyan"

    $devices = @()

    # Get USB devices via WMI
    $usbDevices = Get-WmiObject -Class Win32_PnPEntity | Where-Object {
        $_.DeviceID -like "USB\VID_*"
    }

    foreach ($device in $usbDevices) {
        if ($device.DeviceID -match "USB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})") {
            $vid = $Matches[1]
            $pid = $Matches[2]

            $devices += [PSCustomObject]@{
                Name = $device.Name
                VendorId = $vid
                ProductId = $pid
                Status = $device.Status
                DeviceID = $device.DeviceID
            }
        }
    }

    return $devices
}

function Test-IsPrinter {
    param([string]$Name)

    $printerKeywords = @(
        "printer", "print", "POS", "thermal", "receipt",
        "XP-", "XPRINTER", "EPSON", "STAR", "CITIZEN",
        "58mm", "80mm", "USB Printing"
    )

    foreach ($keyword in $printerKeywords) {
        if ($Name -like "*$keyword*") {
            return $true
        }
    }
    return $false
}

function Download-Zadig {
    $zadigPath = Join-Path $PSScriptRoot "zadig.exe"

    if (Test-Path $zadigPath) {
        Write-Log "Zadig already downloaded" "Green"
        return $zadigPath
    }

    Write-Log "Downloading Zadig..." "Yellow"

    # Zadig download URL (latest version)
    $zadigUrl = "https://github.com/pbatard/libwdi/releases/download/v1.5.0/zadig-2.8.exe"

    try {
        Invoke-WebRequest -Uri $zadigUrl -OutFile $zadigPath -UseBasicParsing
        Write-Log "Zadig downloaded successfully" "Green"
        return $zadigPath
    }
    catch {
        Write-Log "Failed to download Zadig: $_" "Red"
        Write-Log "Please download manually from https://zadig.akeo.ie/" "Yellow"
        return $null
    }
}

function Install-WinUsbDriver {
    param(
        [string]$Vid,
        [string]$Pid,
        [string]$DeviceName
    )

    Write-Log "Installing WinUSB driver for $DeviceName (VID:$Vid PID:$Pid)..." "Cyan"

    # Download Zadig if needed
    $zadigPath = Download-Zadig

    if (-not $zadigPath) {
        Write-Log "Cannot proceed without Zadig" "Red"
        return $false
    }

    # Create INF file for the device
    $infContent = @"
; OpenPrint WinUSB Driver INF
; Generated for VID:$Vid PID:$Pid

[Version]
Signature   = "`$Windows NT`$"
Class       = USBDevice
ClassGUID   = {88BAE032-5A81-49f0-BC3D-A4FF138216D6}
Provider    = %ManufacturerName%
CatalogFile = openprint_winusb.cat
DriverVer   = 01/01/2024,1.0.0.0

[Manufacturer]
%ManufacturerName% = Standard,NTamd64

[Standard.NTamd64]
%DeviceName% = USB_Install, USB\VID_$Vid&PID_$Pid

[USB_Install]
Include = winusb.inf
Needs   = WINUSB.NT

[USB_Install.Services]
Include = winusb.inf
Needs   = WINUSB.NT.Services

[USB_Install.HW]
AddReg = Dev_AddReg

[Dev_AddReg]
HKR,,DeviceInterfaceGUIDs,0x10000,"{a5dcbf10-6530-11d2-901f-00c04fb951ed}"

[Strings]
ManufacturerName = "OpenPrint"
DeviceName       = "$DeviceName"
"@

    $infPath = Join-Path $PSScriptRoot "openprint_$($Vid)_$($Pid).inf"
    $infContent | Out-File -FilePath $infPath -Encoding ASCII

    Write-Log "Created INF file: $infPath" "Green"

    # Instructions for manual installation
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  WinUSB Driver Installation" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Option 1: Use Zadig (Recommended)" -ForegroundColor Yellow
    Write-Host "  1. Zadig will open automatically"
    Write-Host "  2. Select 'Options' -> 'List All Devices'"
    Write-Host "  3. Find your printer: $DeviceName"
    Write-Host "  4. Select 'WinUSB' as the driver"
    Write-Host "  5. Click 'Replace Driver' or 'Install Driver'"
    Write-Host ""
    Write-Host "Option 2: Manual INF installation" -ForegroundColor Yellow
    Write-Host "  1. Open Device Manager"
    Write-Host "  2. Find the printer device"
    Write-Host "  3. Right-click -> Update Driver"
    Write-Host "  4. Browse -> Let me pick -> Have Disk"
    Write-Host "  5. Select: $infPath"
    Write-Host ""

    # Launch Zadig
    Write-Log "Launching Zadig..." "Cyan"
    Start-Process -FilePath $zadigPath

    return $true
}

# Main script logic
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  OpenPrint WinUSB Driver Installer" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# List devices mode
if ($ListDevices -or (-not $VendorId -and -not $ProductId -and -not $Auto)) {
    $devices = Get-UsbDevices

    Write-Host "Connected USB Devices:" -ForegroundColor Yellow
    Write-Host ""

    $printers = @()
    $index = 1

    foreach ($device in $devices) {
        $isPrinter = Test-IsPrinter -Name $device.Name
        $color = if ($isPrinter) { "Green" } else { "Gray" }
        $marker = if ($isPrinter) { "[PRINTER]" } else { "" }

        Write-Host "  $index. $($device.Name) $marker" -ForegroundColor $color
        Write-Host "     VID: $($device.VendorId)  PID: $($device.ProductId)" -ForegroundColor $color
        Write-Host ""

        if ($isPrinter) {
            $printers += @{
                Index = $index
                Device = $device
            }
        }
        $index++
    }

    if ($printers.Count -gt 0) {
        Write-Host "Found $($printers.Count) potential printer(s)" -ForegroundColor Green
        Write-Host ""

        if ($Auto) {
            # Auto-install for all printers
            foreach ($p in $printers) {
                Install-WinUsbDriver -Vid $p.Device.VendorId -Pid $p.Device.ProductId -DeviceName $p.Device.Name
            }
        }
        else {
            Write-Host "To install WinUSB driver, run:" -ForegroundColor Yellow
            foreach ($p in $printers) {
                Write-Host "  .\install-winusb.ps1 -VendorId `"$($p.Device.VendorId)`" -ProductId `"$($p.Device.ProductId)`"" -ForegroundColor Cyan
            }
        }
    }
    else {
        Write-Host "No printers detected. Connect your USB printer and run again." -ForegroundColor Yellow
    }

    exit 0
}

# Install mode
if ($VendorId -and $ProductId) {
    $devices = Get-UsbDevices
    $targetDevice = $devices | Where-Object {
        $_.VendorId -eq $VendorId -and $_.ProductId -eq $ProductId
    } | Select-Object -First 1

    $deviceName = if ($targetDevice) { $targetDevice.Name } else { "USB Printer" }

    Install-WinUsbDriver -Vid $VendorId -Pid $ProductId -DeviceName $deviceName
}
else {
    Write-Log "Please specify -VendorId and -ProductId, or use -ListDevices" "Yellow"
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Cyan
    Write-Host "  .\install-winusb.ps1 -ListDevices"
    Write-Host "  .\install-winusb.ps1 -VendorId `"0483`" -ProductId `"5720`""
    Write-Host "  .\install-winusb.ps1 -Auto"
}
