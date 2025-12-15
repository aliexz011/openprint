# OpenPrint

Proxy server for thermal printers using ESC/POS commands. Designed to work with POS systems like IIKO, RKEEPER, and JOWI.

**Supports both Linux and Windows!**

## Features

- **USB Printer Support**: Auto-discovery of USB thermal printers
  - Linux: Direct access via `/dev/usb/lp*`, `/dev/ttyUSB*`
  - Windows: Direct USB via LibUSB (no drivers needed!) or Windows Spooler
- **Network Printer Support**: TCP/IP connection to network printers (port 9100)
- **Serial Port Support**: COM port printers (Windows)
- **ESC/POS Commands**: Native ESC/POS command generation
- **Cyrillic Support**: CP866 encoding for Russian text
- **Web Interface**: Test printing from browser
- **REST API**: Simple JSON API for integration
- **Windows Service**: Easy deployment on Windows servers
- **systemd Service**: Easy deployment on Linux servers

## Requirements

### Linux
- Ubuntu 22.04 LTS or newer
- .NET 8 Runtime/SDK

### Windows
- Windows 10/11 or Windows Server 2016+
- .NET 8 Runtime/SDK (or use self-contained .exe)

### Printers
- USB or Network thermal printer (XPRINTER, EPSON, etc.)

---

## Quick Start

### Windows Installation

#### Option 1: Download Pre-built Release
```powershell
# Download from releases
# Extract to C:\OpenPrint
# Run OpenPrint.exe
```

#### Option 2: Build from Source
```powershell
# Clone repository
cd OpenPrint

# Build self-contained .exe
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish/win-x64

# Run
.\publish\win-x64\OpenPrint.exe
```

#### Option 3: Install as Windows Service
```powershell
# Run as Administrator
.\install.ps1

# Or manually:
sc.exe create OpenPrint binPath="C:\OpenPrint\OpenPrint.exe" start=auto
sc.exe start OpenPrint
```

### Linux Installation

```bash
# Clone or download the project
cd OpenPrint

# Run installation script (as root)
sudo ./install.sh
```

---

## Windows: USB Printers Without Drivers

OpenPrint can communicate directly with USB printers **without installing manufacturer drivers**!

### How It Works

OpenPrint uses LibUSB to send raw ESC/POS data directly to the USB device, bypassing the Windows Print Spooler.

### Setup for Direct USB Access

#### Step 1: Find Your Printer's VID:PID

```powershell
# List all USB devices
.\drivers\install-winusb.ps1 -ListDevices
```

Or check Device Manager:
1. Open Device Manager
2. Find your printer
3. Properties → Details → Hardware IDs
4. Note the VID and PID (e.g., `USB\VID_0483&PID_5720`)

#### Step 2: Install WinUSB Driver

```powershell
# Automatic (recommended)
.\drivers\install-winusb.ps1 -Auto

# Or for specific device
.\drivers\install-winusb.ps1 -VendorId "0483" -ProductId "5720"
```

This will:
1. Download Zadig (driver installer)
2. Create an INF file for your printer
3. Open Zadig for driver installation

In Zadig:
1. Options → List All Devices
2. Select your printer (e.g., "USB Printing Support")
3. Select **WinUSB** as target driver
4. Click "Replace Driver"

#### Step 3: Configure OpenPrint (Optional)

If auto-discovery doesn't find your printer, add it manually to `appsettings.json`:

```json
{
  "OpenPrint": {
    "LibUsbPrinters": [
      {
        "Name": "My Thermal Printer",
        "VendorId": 1155,
        "ProductId": 22304,
        "Enabled": true
      }
    ]
  }
}
```

> **Note**: VendorId/ProductId should be in decimal. Convert hex to decimal:
> - 0x0483 = 1155
> - 0x5720 = 22304

### Reverting to Original Driver

If you need to restore the original printer driver:
1. Open Device Manager
2. Find the printer (may be under "Universal Serial Bus devices")
3. Right-click → Update Driver
4. Browse → Let me pick from available drivers
5. Select the original manufacturer driver

---

## Configuration

Edit `appsettings.json`:

```json
{
  "OpenPrint": {
    "Port": 5050,
    "Host": "127.0.0.1",
    "AutoDiscoverUSB": true,

    // Windows: Enable/disable discovery methods
    "UseLibUsb": true,
    "UseWindowsSpooler": true,

    // Linux: USB device paths
    "UsbDevicePaths": [
      "/dev/usb/lp*",
      "/dev/ttyUSB*"
    ],

    // Windows: LibUSB printers (direct USB, no drivers)
    "LibUsbPrinters": [
      {
        "Name": "Kitchen Printer",
        "VendorId": 1155,
        "ProductId": 22304,
        "Enabled": false
      }
    ],

    // Windows: Printers with installed drivers
    "WindowsPrinters": [
      {
        "Name": "Bar Printer",
        "WindowsPrinterName": "XPRINTER XP-80C",
        "Enabled": false
      }
    ],

    // Windows: Serial/COM port printers
    "SerialPorts": [
      {
        "Name": "COM Printer",
        "PortName": "COM3",
        "BaudRate": 9600,
        "Enabled": false
      }
    ],

    // Network printers (works on both platforms)
    "NetworkPrinters": [
      {
        "Name": "Network Printer",
        "IpAddress": "192.168.1.100",
        "Port": 9100,
        "Enabled": true
      }
    ],

    "PrintDefaults": {
      "PaperCut": true,
      "Encoding": "CP866"
    }
  }
}
```

---

## API Reference

### Base URL

```
http://127.0.0.1:5050
```

### Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/health` | Service health check |
| `GET` | `/api/printers` | List all printers |
| `POST` | `/api/printers/refresh` | Force refresh printer list |
| `POST` | `/api/print` | Print content |
| `POST` | `/api/print/test` | Print test page |
| `POST` | `/api/printers/{id}/test` | Print test page to specific printer |

### Print Request

```http
POST /api/print
Content-Type: application/json

{
  "printerIdentifier": "usb_0483_5720",
  "content": "Hello World!\nПривет Мир!",
  "options": {
    "fontSize": "normal",
    "alignment": "center",
    "cutPaper": true,
    "encoding": "CP866"
  }
}
```

### Response

```json
{
  "success": true,
  "message": "Print successful",
  "printerUsed": "Kitchen Printer",
  "timestamp": "2025-12-15T10:30:00Z"
}
```

---

## Code Examples

### cURL

```bash
# Health check
curl http://localhost:5050/api/health

# List printers
curl http://localhost:5050/api/printers

# Print
curl -X POST http://localhost:5050/api/print \
  -H "Content-Type: application/json" \
  -d '{"content": "Test print\nТестовая печать", "options": {"cutPaper": true}}'
```

### Python

```python
import requests

client = requests.Session()
base_url = "http://localhost:5050"

# Print receipt
response = client.post(f"{base_url}/api/print", json={
    "content": "Order #123\n\nCoffee x2\nTotal: 200.00",
    "options": {"alignment": "center", "cutPaper": True}
})

print(response.json())
```

### JavaScript

```javascript
const response = await fetch('http://localhost:5050/api/print', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    content: 'Hello World!',
    options: { cutPaper: true }
  })
});

const result = await response.json();
console.log(result);
```

---

## Windows Service Commands

```powershell
# Install service
.\install.ps1

# Uninstall service
.\install.ps1 -Uninstall

# Manual commands
Start-Service OpenPrint
Stop-Service OpenPrint
Restart-Service OpenPrint
Get-Service OpenPrint

# View logs
Get-EventLog -LogName Application -Source OpenPrint -Newest 50
```

## Linux systemd Commands

```bash
sudo systemctl start openprint
sudo systemctl stop openprint
sudo systemctl restart openprint
sudo systemctl status openprint
sudo journalctl -u openprint -f
```

---

## Troubleshooting

### Windows: Printer not detected via LibUSB

1. **Check WinUSB driver is installed**
   ```powershell
   .\drivers\install-winusb.ps1 -ListDevices
   ```

2. **Verify device in Device Manager**
   - Should appear under "Universal Serial Bus devices"
   - NOT under "Printers" (that means Windows driver is active)

3. **Check configuration**
   - Ensure `UseLibUsb: true` in appsettings.json
   - Add printer manually to `LibUsbPrinters` if auto-discovery fails

### Windows: Printer works with Windows driver only

Some printers may not work with WinUSB. Use Windows Spooler mode:

```json
{
  "UseLibUsb": false,
  "UseWindowsSpooler": true,
  "WindowsPrinters": [
    {
      "Name": "My Printer",
      "WindowsPrinterName": "EXACT NAME FROM CONTROL PANEL",
      "Enabled": true
    }
  ]
}
```

### Linux: Permission denied

```bash
sudo usermod -aG lp $USER
sudo usermod -aG dialout $USER
# Logout and login again
```

### Network printer not connecting

```bash
# Test connectivity
nc -zv 192.168.1.100 9100

# Check firewall
sudo ufw allow from 192.168.1.0/24 to any port 9100
```

### Cyrillic text not printing

1. Ensure CP866 encoding in config
2. Some printers need firmware update for Cyrillic
3. Try Windows-1251 encoding as alternative

---

## Supported Printers

Tested with:
- XPRINTER XP-80C
- XPRINTER XP-58
- XPRINTER XP-365B
- EPSON TM-T88V
- POS-58 series
- Star TSP100
- Citizen CT-S310

Most ESC/POS compatible thermal printers should work.

---

## Project Structure

```
OpenPrint/
├── Models/
│   ├── AppSettings.cs      # Configuration classes
│   ├── Printer.cs          # Printer model
│   ├── PrintRequest.cs     # API request model
│   └── PrintResponse.cs    # API response model
├── Services/
│   ├── IPrinterService.cs          # Service interfaces
│   ├── PrinterService.cs           # Main orchestration
│   ├── UsbPrinterDiscovery.cs      # Linux USB discovery
│   ├── WindowsUsbPrinterDiscovery.cs # Windows USB discovery
│   ├── WindowsPrinterManager.cs    # Windows Spooler P/Invoke
│   ├── LibUsbPrinterManager.cs     # Direct USB via LibUSB
│   ├── NetworkPrinterManager.cs    # Network printers
│   ├── EscPosCommandBuilder.cs     # ESC/POS commands
│   └── PrintQueue.cs               # Thread-safe queue
├── wwwroot/
│   └── index.html          # Web interface
├── drivers/
│   └── install-winusb.ps1  # WinUSB driver installer
├── Program.cs              # Entry point
├── appsettings.json        # Configuration
├── install.ps1             # Windows service installer
├── install.sh              # Linux service installer
└── OpenPrint.csproj        # Project file
```

---

## Building

### Windows (self-contained .exe)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish/win-x64
```

Result: `./publish/win-x64/OpenPrint.exe` (~46 MB)

### Linux (self-contained)

```bash
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish/linux-x64
```

### Cross-platform (requires .NET runtime)

```bash
dotnet publish -c Release -o ./publish/portable
```

---

## License

MIT License

## Repository

GitHub: https://github.com/aliexz011/openprint
