# OpenPrint

Proxy server for thermal printers using ESC/POS commands. Designed to work with POS systems like IIKO, RKEEPER, and JOWI.

## Features

- **USB Printer Support**: Auto-discovery of USB thermal printers (`/dev/usb/lp*`, `/dev/ttyUSB*`)
- **Network Printer Support**: TCP/IP connection to network printers (port 9100)
- **ESC/POS Commands**: Native ESC/POS command generation
- **Cyrillic Support**: CP866 encoding for Russian text
- **Web Interface**: Test printing from browser
- **REST API**: Simple JSON API for integration
- **systemd Service**: Easy deployment on Linux servers

## Requirements

- Ubuntu 22.04 LTS or newer
- .NET 8 Runtime/SDK
- USB or Network thermal printer (XPRINTER, EPSON, etc.)

## Quick Start

### Installation

```bash
# Clone or download the project
cd OpenPrint

# Run installation script (as root)
sudo ./install.sh
```

### Manual Build

```bash
# Install .NET 8 SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# Build
dotnet build

# Run
dotnet run
```

### Development

```bash
# Run in development mode
dotnet run --environment Development
```

## Configuration

Edit `appsettings.json`:

```json
{
  "OpenPrint": {
    "Port": 5050,
    "Host": "127.0.0.1",
    "AutoDiscoverUSB": true,
    "NetworkPrinters": [
      {
        "Name": "Kitchen Printer",
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

### Network Printers

Add network printers to the `NetworkPrinters` array:

```json
{
  "Name": "Bar Printer",
  "IpAddress": "192.168.1.101",
  "Port": 9100,
  "Enabled": true
}
```

### USB Permissions

Ensure the service user has access to USB devices:

```bash
sudo usermod -aG lp openprint
sudo usermod -aG dialout openprint
```

---

# Developer Documentation / API Reference

## Base URL

```
http://127.0.0.1:5050
```

By default, OpenPrint listens only on localhost. To integrate from external systems, configure a reverse proxy (nginx) or change the `Host` setting.

## Content Type

All POST requests must include:
```
Content-Type: application/json
```

All responses are returned as JSON with UTF-8 encoding.

---

## Endpoints Overview

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/health` | Service health check |
| `GET` | `/api/printers` | List all printers |
| `POST` | `/api/printers/refresh` | Force refresh printer list |
| `POST` | `/api/print` | Print content |
| `POST` | `/api/print/test` | Print test page (any printer) |
| `POST` | `/api/printers/{id}/test` | Print test page (specific printer) |

---

## 1. Health Check

Check if the service is running and get basic statistics.

### Request

```http
GET /api/health
```

### Response

```json
{
  "status": "healthy",
  "version": "1.0.0",
  "uptime": "2h 15m 30s",
  "printersAvailable": 2,
  "timestamp": "2025-12-14T10:30:00Z"
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | Service status: `"healthy"` |
| `version` | string | OpenPrint version |
| `uptime` | string | Time since service start |
| `printersAvailable` | integer | Number of online printers |
| `timestamp` | string | Current UTC timestamp (ISO 8601) |

---

## 2. List Printers

Get all discovered printers (USB and network).

### Request

```http
GET /api/printers
```

### Response

```json
{
  "printers": [
    {
      "id": "usb_lp0",
      "name": "USB Printer (lp0)",
      "connectionType": "USB",
      "devicePath": "/dev/usb/lp0",
      "ipAddress": null,
      "port": null,
      "status": "online",
      "lastSeen": "2025-12-14T10:30:00Z"
    },
    {
      "id": "lan_192_168_1_100_9100",
      "name": "Kitchen Printer",
      "connectionType": "LAN",
      "devicePath": null,
      "ipAddress": "192.168.1.100",
      "port": 9100,
      "status": "online",
      "lastSeen": "2025-12-14T10:30:00Z"
    }
  ],
  "count": 2
}
```

### Printer Object Fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique printer identifier (use for print requests) |
| `name` | string | Human-readable printer name |
| `connectionType` | string | `"USB"` or `"LAN"` |
| `devicePath` | string\|null | USB device path (only for USB printers) |
| `ipAddress` | string\|null | IP address (only for LAN printers) |
| `port` | integer\|null | TCP port (only for LAN printers, usually 9100) |
| `status` | string | `"online"`, `"offline"`, `"error"`, `"busy"`, `"unknown"` |
| `lastSeen` | string | Last successful connection timestamp (ISO 8601) |

---

## 3. Refresh Printers

Force re-scan of all printers. Useful after connecting a new printer.

### Request

```http
POST /api/printers/refresh
```

### Response

Same format as `GET /api/printers`.

---

## 4. Print Content

Send text content to a printer.

### Request

```http
POST /api/print
Content-Type: application/json
```

### Request Body

```json
{
  "printerIdentifier": "usb_lp0",
  "content": "Order #12345\n================\nCoffee x2     200.00\nTea x1        100.00\n================\nTotal:        300.00\n\nThank you!",
  "options": {
    "fontSize": "normal",
    "alignment": "center",
    "cutPaper": true,
    "bold": false,
    "encoding": "CP866"
  }
}
```

### Request Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `printerIdentifier` | string | No | Printer ID, name, IP, or device path. If omitted, uses first available online printer |
| `content` | string | **Yes** | Text content to print. Use `\n` for line breaks |
| `options` | object | No | Print formatting options |

### Options Object

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `fontSize` | string | `"normal"` | Font size: `"small"`, `"normal"`, `"large"` |
| `alignment` | string | `"left"` | Text alignment: `"left"`, `"center"`, `"right"` |
| `cutPaper` | boolean | `true` | Cut paper after printing |
| `bold` | boolean | `false` | Enable bold text |
| `encoding` | string | `"CP866"` | Text encoding: `"CP866"`, `"Windows-1251"`, `"UTF-8"` |

### Success Response

```json
{
  "success": true,
  "message": "Print successful",
  "printerUsed": "USB Printer (lp0)",
  "timestamp": "2025-12-14T10:30:00Z"
}
```

### Error Response

```json
{
  "success": false,
  "message": "Printer not found: invalid_printer",
  "printerUsed": null,
  "timestamp": "2025-12-14T10:30:00Z",
  "error": "No printer matching 'invalid_printer' was found"
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `success` | boolean | `true` if print was successful |
| `message` | string | Human-readable status message |
| `printerUsed` | string\|null | Name of printer that was used |
| `timestamp` | string | Operation timestamp (ISO 8601) |
| `error` | string\|null | Error details (only on failure) |

---

## 5. Print Test Page

Print a test page to verify printer functionality.

### To First Available Printer

```http
POST /api/print/test
```

### To Specific Printer

```http
POST /api/printers/{printerId}/test
```

**Example:**
```http
POST /api/printers/usb_lp0/test
```

### Response

Same format as print response.

### Test Page Content

```
================================
        OPENPRINT TEST
================================

Printer: [Printer Name]
Time: 2025-12-14 10:30:00
Status: OK

Hello World!
Привет Мир!

================================
```

---

## Error Codes

All endpoints return HTTP 200 with `success: false` for business logic errors.
HTTP 500 is returned only for unexpected server errors.

| Error Message | Cause | Solution |
|--------------|-------|----------|
| `Content is required` | Empty or missing `content` field | Provide text content |
| `No available printers found` | No online printers | Check printer connections |
| `Printer not found: {id}` | Invalid printer identifier | Use ID from `/api/printers` |
| `Printer is offline: {name}` | Printer not responding | Check printer power/connection |
| `Printer is busy` | Another job in progress | Wait and retry |
| `Print operation timed out` | Printer not responding | Check connection, restart printer |
| `Permission denied for device` | No access to USB device | Add user to `lp` group |

---

## Code Examples

### cURL

```bash
# Health check
curl -s http://localhost:5050/api/health | jq

# List printers
curl -s http://localhost:5050/api/printers | jq

# Print receipt
curl -X POST http://localhost:5050/api/print \
  -H "Content-Type: application/json" \
  -d '{
    "content": "================================\n        RECEIPT #12345\n================================\n\nCoffee Latte      x2    400.00\nCroissant         x1    150.00\n--------------------------------\nSubtotal:               550.00\nTax (10%):               55.00\n================================\nTOTAL:                  605.00\n================================\n\n     Thank you for visiting!\n        Have a nice day!\n",
    "options": {
      "alignment": "left",
      "cutPaper": true
    }
  }'

# Print to specific printer
curl -X POST http://localhost:5050/api/print \
  -H "Content-Type: application/json" \
  -d '{
    "printerIdentifier": "lan_192_168_1_100_9100",
    "content": "Kitchen Order #123\n\n2x Coffee\n1x Tea",
    "options": {"cutPaper": true}
  }'

# Test print
curl -X POST http://localhost:5050/api/printers/usb_lp0/test
```

---

### Python

```python
import requests
from typing import Optional

class OpenPrintClient:
    def __init__(self, base_url: str = "http://localhost:5050"):
        self.base_url = base_url

    def health(self) -> dict:
        """Check service health"""
        response = requests.get(f"{self.base_url}/api/health")
        return response.json()

    def get_printers(self) -> list:
        """Get list of all printers"""
        response = requests.get(f"{self.base_url}/api/printers")
        return response.json()["printers"]

    def print_text(
        self,
        content: str,
        printer_id: Optional[str] = None,
        alignment: str = "left",
        font_size: str = "normal",
        cut_paper: bool = True,
        bold: bool = False,
        encoding: str = "CP866"
    ) -> dict:
        """Print text content"""
        payload = {
            "content": content,
            "options": {
                "alignment": alignment,
                "fontSize": font_size,
                "cutPaper": cut_paper,
                "bold": bold,
                "encoding": encoding
            }
        }
        if printer_id:
            payload["printerIdentifier"] = printer_id

        response = requests.post(
            f"{self.base_url}/api/print",
            json=payload
        )
        return response.json()

    def print_test(self, printer_id: Optional[str] = None) -> dict:
        """Print test page"""
        if printer_id:
            url = f"{self.base_url}/api/printers/{printer_id}/test"
        else:
            url = f"{self.base_url}/api/print/test"

        response = requests.post(url)
        return response.json()


# Usage example
if __name__ == "__main__":
    client = OpenPrintClient()

    # Check health
    health = client.health()
    print(f"Status: {health['status']}, Printers: {health['printersAvailable']}")

    # List printers
    printers = client.get_printers()
    for p in printers:
        print(f"- {p['name']} ({p['connectionType']}): {p['status']}")

    # Print receipt
    receipt = """
================================
      COFFEE SHOP
================================

Latte           x2    400.00
Espresso        x1    150.00
--------------------------------
TOTAL:                550.00

      Thank you!
================================
"""

    result = client.print_text(
        content=receipt,
        alignment="center",
        cut_paper=True
    )

    if result["success"]:
        print(f"Printed on: {result['printerUsed']}")
    else:
        print(f"Error: {result['message']}")
```

---

### JavaScript / Node.js

```javascript
class OpenPrintClient {
  constructor(baseUrl = 'http://localhost:5050') {
    this.baseUrl = baseUrl;
  }

  async health() {
    const response = await fetch(`${this.baseUrl}/api/health`);
    return response.json();
  }

  async getPrinters() {
    const response = await fetch(`${this.baseUrl}/api/printers`);
    const data = await response.json();
    return data.printers;
  }

  async print(content, options = {}) {
    const payload = {
      content,
      options: {
        alignment: options.alignment || 'left',
        fontSize: options.fontSize || 'normal',
        cutPaper: options.cutPaper !== false,
        bold: options.bold || false,
        encoding: options.encoding || 'CP866'
      }
    };

    if (options.printerId) {
      payload.printerIdentifier = options.printerId;
    }

    const response = await fetch(`${this.baseUrl}/api/print`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    return response.json();
  }

  async printTest(printerId = null) {
    const url = printerId
      ? `${this.baseUrl}/api/printers/${encodeURIComponent(printerId)}/test`
      : `${this.baseUrl}/api/print/test`;

    const response = await fetch(url, { method: 'POST' });
    return response.json();
  }
}

// Usage example
async function main() {
  const client = new OpenPrintClient();

  // Check health
  const health = await client.health();
  console.log(`Status: ${health.status}, Printers: ${health.printersAvailable}`);

  // List printers
  const printers = await client.getPrinters();
  printers.forEach(p => {
    console.log(`- ${p.name} (${p.connectionType}): ${p.status}`);
  });

  // Print receipt
  const receipt = `
================================
      COFFEE SHOP
================================

Latte           x2    400.00
Espresso        x1    150.00
--------------------------------
TOTAL:                550.00

      Thank you!
================================
`;

  const result = await client.print(receipt, {
    alignment: 'center',
    cutPaper: true
  });

  if (result.success) {
    console.log(`Printed on: ${result.printerUsed}`);
  } else {
    console.error(`Error: ${result.message}`);
  }
}

main().catch(console.error);
```

---

### PHP

```php
<?php

class OpenPrintClient {
    private string $baseUrl;

    public function __construct(string $baseUrl = 'http://localhost:5050') {
        $this->baseUrl = $baseUrl;
    }

    public function health(): array {
        return $this->get('/api/health');
    }

    public function getPrinters(): array {
        $response = $this->get('/api/printers');
        return $response['printers'] ?? [];
    }

    public function printText(
        string $content,
        ?string $printerId = null,
        string $alignment = 'left',
        string $fontSize = 'normal',
        bool $cutPaper = true,
        bool $bold = false,
        string $encoding = 'CP866'
    ): array {
        $payload = [
            'content' => $content,
            'options' => [
                'alignment' => $alignment,
                'fontSize' => $fontSize,
                'cutPaper' => $cutPaper,
                'bold' => $bold,
                'encoding' => $encoding
            ]
        ];

        if ($printerId !== null) {
            $payload['printerIdentifier'] = $printerId;
        }

        return $this->post('/api/print', $payload);
    }

    public function printTest(?string $printerId = null): array {
        $url = $printerId
            ? "/api/printers/" . urlencode($printerId) . "/test"
            : "/api/print/test";

        return $this->post($url);
    }

    private function get(string $endpoint): array {
        $ch = curl_init($this->baseUrl . $endpoint);
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
        curl_setopt($ch, CURLOPT_TIMEOUT, 30);

        $response = curl_exec($ch);
        curl_close($ch);

        return json_decode($response, true) ?? [];
    }

    private function post(string $endpoint, ?array $data = null): array {
        $ch = curl_init($this->baseUrl . $endpoint);
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
        curl_setopt($ch, CURLOPT_POST, true);
        curl_setopt($ch, CURLOPT_TIMEOUT, 30);

        if ($data !== null) {
            curl_setopt($ch, CURLOPT_HTTPHEADER, ['Content-Type: application/json']);
            curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($data));
        }

        $response = curl_exec($ch);
        curl_close($ch);

        return json_decode($response, true) ?? [];
    }
}

// Usage example
$client = new OpenPrintClient();

// Check health
$health = $client->health();
echo "Status: {$health['status']}, Printers: {$health['printersAvailable']}\n";

// List printers
$printers = $client->getPrinters();
foreach ($printers as $printer) {
    echo "- {$printer['name']} ({$printer['connectionType']}): {$printer['status']}\n";
}

// Print receipt
$receipt = <<<EOT
================================
      COFFEE SHOP
================================

Latte           x2    400.00
Espresso        x1    150.00
--------------------------------
TOTAL:                550.00

      Thank you!
================================
EOT;

$result = $client->printText($receipt, null, 'center', 'normal', true);

if ($result['success']) {
    echo "Printed on: {$result['printerUsed']}\n";
} else {
    echo "Error: {$result['message']}\n";
}
```

---

### C# / .NET

```csharp
using System.Net.Http.Json;
using System.Text.Json;

public class OpenPrintClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenPrintClient(string baseUrl = "http://localhost:5050")
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<HealthResponse> HealthAsync()
    {
        return await _httpClient.GetFromJsonAsync<HealthResponse>("/api/health", _jsonOptions)
            ?? throw new Exception("Failed to get health");
    }

    public async Task<List<PrinterDto>> GetPrintersAsync()
    {
        var response = await _httpClient.GetFromJsonAsync<PrintersListResponse>("/api/printers", _jsonOptions);
        return response?.Printers ?? new List<PrinterDto>();
    }

    public async Task<PrintResponse> PrintAsync(
        string content,
        string? printerId = null,
        string alignment = "left",
        string fontSize = "normal",
        bool cutPaper = true,
        bool bold = false,
        string encoding = "CP866")
    {
        var request = new PrintRequest
        {
            PrinterIdentifier = printerId,
            Content = content,
            Options = new PrintOptions
            {
                Alignment = alignment,
                FontSize = fontSize,
                CutPaper = cutPaper,
                Bold = bold,
                Encoding = encoding
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/api/print", request, _jsonOptions);
        return await response.Content.ReadFromJsonAsync<PrintResponse>(_jsonOptions)
            ?? throw new Exception("Failed to parse response");
    }

    public async Task<PrintResponse> PrintTestAsync(string? printerId = null)
    {
        var url = printerId != null
            ? $"/api/printers/{Uri.EscapeDataString(printerId)}/test"
            : "/api/print/test";

        var response = await _httpClient.PostAsync(url, null);
        return await response.Content.ReadFromJsonAsync<PrintResponse>(_jsonOptions)
            ?? throw new Exception("Failed to parse response");
    }

    public void Dispose() => _httpClient.Dispose();
}

// DTOs
public record HealthResponse(string Status, string Version, string Uptime, int PrintersAvailable, DateTime Timestamp);
public record PrinterDto(string Id, string Name, string ConnectionType, string? DevicePath, string? IpAddress, int? Port, string Status, DateTime LastSeen);
public record PrintersListResponse(List<PrinterDto> Printers, int Count);
public record PrintOptions(string? Alignment, string? FontSize, bool? CutPaper, bool? Bold, string? Encoding);
public record PrintRequest(string Content, string? PrinterIdentifier, PrintOptions? Options);
public record PrintResponse(bool Success, string Message, string? PrinterUsed, DateTime Timestamp, string? Error);

// Usage example
class Program
{
    static async Task Main()
    {
        using var client = new OpenPrintClient();

        // Check health
        var health = await client.HealthAsync();
        Console.WriteLine($"Status: {health.Status}, Printers: {health.PrintersAvailable}");

        // List printers
        var printers = await client.GetPrintersAsync();
        foreach (var p in printers)
        {
            Console.WriteLine($"- {p.Name} ({p.ConnectionType}): {p.Status}");
        }

        // Print receipt
        var receipt = @"
================================
      COFFEE SHOP
================================

Latte           x2    400.00
Espresso        x1    150.00
--------------------------------
TOTAL:                550.00

      Thank you!
================================
";

        var result = await client.PrintAsync(receipt, alignment: "center", cutPaper: true);

        if (result.Success)
            Console.WriteLine($"Printed on: {result.PrinterUsed}");
        else
            Console.WriteLine($"Error: {result.Message}");
    }
}
```

---

### Go

```go
package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"
)

type OpenPrintClient struct {
	BaseURL string
}

type HealthResponse struct {
	Status            string `json:"status"`
	Version           string `json:"version"`
	Uptime            string `json:"uptime"`
	PrintersAvailable int    `json:"printersAvailable"`
	Timestamp         string `json:"timestamp"`
}

type Printer struct {
	ID             string `json:"id"`
	Name           string `json:"name"`
	ConnectionType string `json:"connectionType"`
	DevicePath     string `json:"devicePath,omitempty"`
	IPAddress      string `json:"ipAddress,omitempty"`
	Port           int    `json:"port,omitempty"`
	Status         string `json:"status"`
	LastSeen       string `json:"lastSeen"`
}

type PrintersResponse struct {
	Printers []Printer `json:"printers"`
	Count    int       `json:"count"`
}

type PrintOptions struct {
	Alignment string `json:"alignment,omitempty"`
	FontSize  string `json:"fontSize,omitempty"`
	CutPaper  bool   `json:"cutPaper"`
	Bold      bool   `json:"bold"`
	Encoding  string `json:"encoding,omitempty"`
}

type PrintRequest struct {
	PrinterIdentifier string       `json:"printerIdentifier,omitempty"`
	Content           string       `json:"content"`
	Options           PrintOptions `json:"options,omitempty"`
}

type PrintResponse struct {
	Success     bool   `json:"success"`
	Message     string `json:"message"`
	PrinterUsed string `json:"printerUsed,omitempty"`
	Timestamp   string `json:"timestamp"`
	Error       string `json:"error,omitempty"`
}

func NewClient(baseURL string) *OpenPrintClient {
	if baseURL == "" {
		baseURL = "http://localhost:5050"
	}
	return &OpenPrintClient{BaseURL: baseURL}
}

func (c *OpenPrintClient) Health() (*HealthResponse, error) {
	resp, err := http.Get(c.BaseURL + "/api/health")
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var health HealthResponse
	if err := json.NewDecoder(resp.Body).Decode(&health); err != nil {
		return nil, err
	}
	return &health, nil
}

func (c *OpenPrintClient) GetPrinters() ([]Printer, error) {
	resp, err := http.Get(c.BaseURL + "/api/printers")
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var result PrintersResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, err
	}
	return result.Printers, nil
}

func (c *OpenPrintClient) Print(content string, printerID string, options PrintOptions) (*PrintResponse, error) {
	request := PrintRequest{
		PrinterIdentifier: printerID,
		Content:           content,
		Options:           options,
	}

	body, _ := json.Marshal(request)
	resp, err := http.Post(c.BaseURL+"/api/print", "application/json", bytes.NewBuffer(body))
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var result PrintResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, err
	}
	return &result, nil
}

func (c *OpenPrintClient) PrintTest(printerID string) (*PrintResponse, error) {
	endpoint := "/api/print/test"
	if printerID != "" {
		endpoint = "/api/printers/" + url.PathEscape(printerID) + "/test"
	}

	resp, err := http.Post(c.BaseURL+endpoint, "application/json", nil)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var result PrintResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, err
	}
	return &result, nil
}

func main() {
	client := NewClient("")

	// Health check
	health, _ := client.Health()
	fmt.Printf("Status: %s, Printers: %d\n", health.Status, health.PrintersAvailable)

	// List printers
	printers, _ := client.GetPrinters()
	for _, p := range printers {
		fmt.Printf("- %s (%s): %s\n", p.Name, p.ConnectionType, p.Status)
	}

	// Print receipt
	receipt := `
================================
      COFFEE SHOP
================================

Latte           x2    400.00
Espresso        x1    150.00
--------------------------------
TOTAL:                550.00

      Thank you!
================================
`

	result, _ := client.Print(receipt, "", PrintOptions{
		Alignment: "center",
		CutPaper:  true,
		Encoding:  "CP866",
	})

	if result.Success {
		fmt.Printf("Printed on: %s\n", result.PrinterUsed)
	} else {
		fmt.Printf("Error: %s\n", result.Message)
	}
}
```

---

## Integration Tips

### 1. Printer Selection Strategy

```python
# Option 1: Use first available (simplest)
client.print_text("Receipt content")

# Option 2: Use specific printer by ID
client.print_text("Kitchen order", printer_id="lan_192_168_1_100_9100")

# Option 3: Select by connection type
printers = client.get_printers()
usb_printer = next((p for p in printers if p['connectionType'] == 'USB' and p['status'] == 'online'), None)
if usb_printer:
    client.print_text("Receipt", printer_id=usb_printer['id'])
```

### 2. Error Handling

```python
result = client.print_text("Content")

if not result['success']:
    error_msg = result.get('error') or result['message']

    if 'not found' in error_msg.lower():
        # Refresh printer list and retry
        client.refresh_printers()
    elif 'offline' in error_msg.lower():
        # Notify admin about printer issue
        notify_admin(f"Printer offline: {error_msg}")
    elif 'busy' in error_msg.lower():
        # Retry after delay
        time.sleep(2)
        result = client.print_text("Content")
```

### 3. Receipt Formatting

```python
def format_receipt(order):
    width = 32  # Standard 58mm paper width in characters

    lines = [
        "=" * width,
        f"{'ORDER #' + str(order['id']):^{width}}",
        "=" * width,
        "",
    ]

    for item in order['items']:
        name = item['name'][:20]
        qty = f"x{item['qty']}"
        price = f"{item['price']:.2f}"
        lines.append(f"{name:<20}{qty:>4}{price:>8}")

    lines.extend([
        "-" * width,
        f"{'TOTAL:':<24}{order['total']:>8.2f}",
        "=" * width,
        "",
        f"{'Thank you!':^{width}}",
    ])

    return "\n".join(lines)
```

### 4. Health Monitoring

```python
import time

def monitor_openprint():
    while True:
        try:
            health = client.health()

            if health['printersAvailable'] == 0:
                alert("No printers available!")

        except requests.RequestException:
            alert("OpenPrint service is down!")

        time.sleep(60)
```

---

## Supported Printers

Tested with:
- XPRINTER XP-80C
- XPRINTER XP-58
- XPRINTER XP-365B
- EPSON TM-T88V
- POS-58 series

Most ESC/POS compatible thermal printers should work.

---

## Troubleshooting

### Printer not detected

1. Check USB connection:
   ```bash
   ls -la /dev/usb/lp*
   ls -la /dev/ttyUSB*
   ```

2. Check permissions:
   ```bash
   sudo usermod -aG lp $USER
   sudo usermod -aG dialout $USER
   # Logout and login again
   ```

3. For systemd service:
   ```bash
   sudo usermod -aG lp openprint
   sudo systemctl restart openprint
   ```

### Network printer not connecting

1. Test connectivity:
   ```bash
   nc -zv 192.168.1.100 9100
   ```

2. Check firewall:
   ```bash
   sudo ufw allow from 192.168.1.0/24 to any port 9100
   ```

### Cyrillic text not printing correctly

1. Ensure CP866 encoding in config
2. Some printers need firmware update for Cyrillic support
3. Try Windows-1251 encoding as alternative

### Service not starting

```bash
# Check logs
sudo journalctl -u openprint -n 100 --no-pager

# Check status
sudo systemctl status openprint
```

---

## systemd Commands

```bash
# Start service
sudo systemctl start openprint

# Stop service
sudo systemctl stop openprint

# Restart service
sudo systemctl restart openprint

# View status
sudo systemctl status openprint

# View logs
sudo journalctl -u openprint -f

# Enable at boot
sudo systemctl enable openprint

# Disable at boot
sudo systemctl disable openprint
```

---

## Repository

GitHub: https://github.com/aliexz011/openprint

## License

MIT License
