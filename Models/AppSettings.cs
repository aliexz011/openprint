namespace OpenPrint.Models;

/// <summary>
/// Strongly-typed configuration for OpenPrint
/// </summary>
public class OpenPrintSettings
{
    public const string SectionName = "OpenPrint";

    public int Port { get; set; } = 5050;
    public string Host { get; set; } = "127.0.0.1";
    public bool AutoDiscoverUSB { get; set; } = true;

    /// <summary>
    /// USB device paths for Linux (ignored on Windows)
    /// </summary>
    public List<string> UsbDevicePaths { get; set; } = new() { "/dev/usb/lp*", "/dev/ttyUSB*" };

    /// <summary>
    /// Enable LibUSB for direct USB access without drivers (recommended for Windows)
    /// Requires WinUSB driver to be installed via Zadig
    /// </summary>
    public bool UseLibUsb { get; set; } = true;

    /// <summary>
    /// Enable Windows Print Spooler for printers with installed drivers
    /// </summary>
    public bool UseWindowsSpooler { get; set; } = true;

    /// <summary>
    /// Windows-specific: Explicitly configured Windows printers (via spooler)
    /// </summary>
    public List<WindowsPrinterConfig> WindowsPrinters { get; set; } = new();

    /// <summary>
    /// LibUSB printers configured by VID:PID (direct USB access)
    /// </summary>
    public List<LibUsbPrinterConfig> LibUsbPrinters { get; set; } = new();

    /// <summary>
    /// Serial/COM port printers
    /// </summary>
    public List<SerialPortConfig> SerialPorts { get; set; } = new();

    /// <summary>
    /// Printer name patterns to match when auto-discovering (case-insensitive)
    /// Examples: "XP-", "POS", "THERMAL", "RECEIPT"
    /// </summary>
    public List<string> PrinterNamePatterns { get; set; } = new() { "XP", "POS", "THERMAL", "RECEIPT", "ESC" };

    public List<NetworkPrinterConfig> NetworkPrinters { get; set; } = new();
    public PrintDefaultsConfig PrintDefaults { get; set; } = new();
    public ConnectionConfig Connection { get; set; } = new();
    public DiscoveryConfig Discovery { get; set; } = new();
}

/// <summary>
/// LibUSB printer configuration (direct USB access by VID:PID)
/// </summary>
public class LibUsbPrinterConfig
{
    /// <summary>
    /// Display name for the printer
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// USB Vendor ID (hex, e.g., 0x0483 or 1155)
    /// </summary>
    public int VendorId { get; set; }

    /// <summary>
    /// USB Product ID (hex, e.g., 0x5720 or 22304)
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// Whether this printer is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Windows-specific printer configuration (via spooler)
/// </summary>
public class WindowsPrinterConfig
{
    /// <summary>
    /// Display name for the printer
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Exact Windows printer name (as shown in Control Panel > Devices and Printers)
    /// </summary>
    public string WindowsPrinterName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this printer is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Serial (COM) port printer configuration
/// </summary>
public class SerialPortConfig
{
    /// <summary>
    /// Display name for the printer
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// COM port name (e.g., "COM3", "COM4")
    /// </summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>
    /// Baud rate (default: 9600)
    /// </summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>
    /// Parity (None, Odd, Even, Mark, Space)
    /// </summary>
    public string Parity { get; set; } = "None";

    /// <summary>
    /// Data bits (default: 8)
    /// </summary>
    public int DataBits { get; set; } = 8;

    /// <summary>
    /// Stop bits (None, One, Two, OnePointFive)
    /// </summary>
    public string StopBits { get; set; } = "One";

    /// <summary>
    /// Whether this printer is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

public class NetworkPrinterConfig
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 9100;
    public bool Enabled { get; set; } = true;
}

public class PrintDefaultsConfig
{
    public bool PaperCut { get; set; } = true;
    public string Encoding { get; set; } = "CP866";
    public int LineSpacing { get; set; } = 30;
    public string FontSize { get; set; } = "normal";
    public string Alignment { get; set; } = "left";
}

public class ConnectionConfig
{
    public int TimeoutMs { get; set; } = 5000;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 500;
    public int PrintTimeoutMs { get; set; } = 30000;
}

public class DiscoveryConfig
{
    public int CacheRefreshIntervalSeconds { get; set; } = 30;
}

public class LoggingFileConfig
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "C:\\ProgramData\\OpenPrint\\logs\\app.log";
}
