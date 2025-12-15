using OpenPrint.Models;
using Microsoft.Extensions.Options;
using System.Management;
using System.Drawing.Printing;
using System.IO.Ports;

namespace OpenPrint.Services;

/// <summary>
/// Discovers and manages USB-connected thermal printers on Windows
/// Supports multiple modes:
/// - LibUSB: Direct USB access without drivers (recommended)
/// - WinSpooler: Through Windows Print Spooler (requires installed printer)
/// - Serial: COM port printers
/// </summary>
public class WindowsUsbPrinterDiscovery : IUsbPrinterDiscovery
{
    private readonly ILogger<WindowsUsbPrinterDiscovery> _logger;
    private readonly OpenPrintSettings _settings;
    private readonly LibUsbPrinterManager _libUsbManager;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Cache of discovered libusb printers (VID:PID -> PrinterInfo)
    private readonly Dictionary<string, LibUsbPrinterInfo> _libUsbPrinters = new();

    // Common POS/thermal printer name patterns
    private static readonly string[] ThermalPrinterPatterns = new[]
    {
        "XP", "XPRINTER", "POS", "EPSON", "THERMAL", "RECEIPT", "ESC", "58MM", "80MM",
        "STAR", "CITIZEN", "BIXOLON", "SEWOO", "RONGTA", "GAINSCHA", "HPRT", "TSC"
    };

    public WindowsUsbPrinterDiscovery(ILogger<WindowsUsbPrinterDiscovery> logger, IOptions<OpenPrintSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _libUsbManager = new LibUsbPrinterManager(logger as ILogger ?? logger);
    }

    public async Task<IEnumerable<Printer>> DiscoverAsync()
    {
        var printers = new List<Printer>();

        if (!_settings.AutoDiscoverUSB)
        {
            _logger.LogDebug("USB auto-discovery is disabled");
            return printers;
        }

        // 1. Discover via LibUSB (direct USB access, no drivers needed)
        if (_settings.UseLibUsb)
        {
            var libUsbPrinters = await DiscoverViaLibUsbAsync();
            printers.AddRange(libUsbPrinters);
        }

        // 2. Discover via Windows spooler (requires installed printers)
        if (_settings.UseWindowsSpooler)
        {
            var wmiPrinters = await DiscoverViaWmiAsync();
            var systemPrinters = await DiscoverViaSystemDrawingAsync();

            // Merge results, avoiding duplicates
            var seenNames = new HashSet<string>(printers.Select(p => p.Name ?? p.Id), StringComparer.OrdinalIgnoreCase);

            foreach (var printer in wmiPrinters.Concat(systemPrinters))
            {
                if (seenNames.Add(printer.Name ?? printer.Id))
                    printers.Add(printer);
            }
        }

        // 3. Add configured Windows printers (explicit configuration)
        var existingIds = new HashSet<string>(printers.Select(p => p.Id));
        foreach (var config in _settings.WindowsPrinters.Where(p => p.Enabled))
        {
            var printerId = $"win_{SanitizeId(config.WindowsPrinterName)}";
            if (!existingIds.Contains(printerId))
            {
                var accessible = WindowsPrinterManager.IsPrinterAccessible(config.WindowsPrinterName);
                printers.Add(new Printer
                {
                    Id = printerId,
                    Name = config.Name,
                    ConnectionType = PrinterConnectionType.USB,
                    DevicePath = config.WindowsPrinterName,
                    Status = accessible ? PrinterStatus.Online : PrinterStatus.Offline,
                    LastSeen = DateTime.UtcNow
                });
            }
        }

        // 4. Add configured LibUSB printers (explicit VID:PID configuration)
        foreach (var config in _settings.LibUsbPrinters.Where(p => p.Enabled))
        {
            var printerId = $"usb_{config.VendorId:X4}_{config.ProductId:X4}";
            if (!existingIds.Contains(printerId))
            {
                var accessible = _libUsbManager.IsDeviceAccessible(config.VendorId, config.ProductId);
                printers.Add(new Printer
                {
                    Id = printerId,
                    Name = config.Name,
                    ConnectionType = PrinterConnectionType.USB,
                    DevicePath = $"{config.VendorId:X4}:{config.ProductId:X4}",
                    Status = accessible ? PrinterStatus.Online : PrinterStatus.Offline,
                    LastSeen = DateTime.UtcNow
                });

                // Cache for later use
                _libUsbPrinters[$"{config.VendorId:X4}:{config.ProductId:X4}"] = new LibUsbPrinterInfo
                {
                    VendorId = config.VendorId,
                    ProductId = config.ProductId,
                    ProductName = config.Name,
                    DeviceId = printerId
                };
            }
        }

        // 5. Add serial port printers
        var serialPrinters = await DiscoverSerialPortsAsync();
        printers.AddRange(serialPrinters);

        _logger.LogInformation("Discovered {Count} printers on Windows (LibUSB: {LibUsb}, Spooler: {Spooler})",
            printers.Count,
            _settings.UseLibUsb ? "enabled" : "disabled",
            _settings.UseWindowsSpooler ? "enabled" : "disabled");

        return printers;
    }

    private async Task<IEnumerable<Printer>> DiscoverViaLibUsbAsync()
    {
        var printers = new List<Printer>();

        try
        {
            await Task.Run(() =>
            {
                var libUsbDevices = _libUsbManager.DiscoverPrinters();

                foreach (var device in libUsbDevices)
                {
                    var devicePath = device.GetDevicePath();
                    var accessible = _libUsbManager.IsDeviceAccessible(device.VendorId, device.ProductId);

                    var printer = new Printer
                    {
                        Id = device.DeviceId,
                        Name = $"{device.ProductName} (USB Direct)",
                        ConnectionType = PrinterConnectionType.USB,
                        DevicePath = devicePath, // Format: "VID:PID"
                        Status = accessible ? PrinterStatus.Online : PrinterStatus.Offline,
                        LastSeen = DateTime.UtcNow
                    };

                    printers.Add(printer);

                    // Cache for later use
                    _libUsbPrinters[devicePath] = device;

                    _logger.LogDebug("Found LibUSB printer: {Name} at {Path}", printer.Name, devicePath);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering printers via LibUSB. Install WinUSB driver using Zadig for direct USB access.");
        }

        return printers;
    }

    private async Task<IEnumerable<Printer>> DiscoverViaWmiAsync()
    {
        var printers = new List<Printer>();

        try
        {
            await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
                foreach (ManagementObject printerObj in searcher.Get())
                {
                    var name = printerObj["Name"]?.ToString();
                    var portName = printerObj["PortName"]?.ToString();
                    var isNetwork = printerObj["Network"] as bool? ?? false;

                    if (string.IsNullOrEmpty(name))
                        continue;

                    // Skip network printers (handled separately)
                    if (isNetwork)
                        continue;

                    // Check if it looks like a thermal/POS printer
                    bool isThermalPrinter = IsThermalPrinter(name);

                    if (isThermalPrinter)
                    {
                        var accessible = WindowsPrinterManager.IsPrinterAccessible(name);
                        var printer = new Printer
                        {
                            Id = $"win_{SanitizeId(name)}",
                            Name = $"{name} (Windows)",
                            ConnectionType = PrinterConnectionType.USB,
                            DevicePath = name,
                            Status = accessible ? PrinterStatus.Online : PrinterStatus.Offline,
                            LastSeen = DateTime.UtcNow
                        };

                        printers.Add(printer);
                        _logger.LogDebug("Found WMI printer: {Name} on port {Port}", name, portName);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering printers via WMI");
        }

        return printers;
    }

    private Task<IEnumerable<Printer>> DiscoverViaSystemDrawingAsync()
    {
        var printers = new List<Printer>();

        try
        {
            foreach (string printerName in PrinterSettings.InstalledPrinters)
            {
                if (IsThermalPrinter(printerName))
                {
                    var accessible = WindowsPrinterManager.IsPrinterAccessible(printerName);
                    printers.Add(new Printer
                    {
                        Id = $"win_{SanitizeId(printerName)}",
                        Name = $"{printerName} (Windows)",
                        ConnectionType = PrinterConnectionType.USB,
                        DevicePath = printerName,
                        Status = accessible ? PrinterStatus.Online : PrinterStatus.Offline,
                        LastSeen = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering printers via System.Drawing.Printing");
        }

        return Task.FromResult<IEnumerable<Printer>>(printers);
    }

    private async Task<IEnumerable<Printer>> DiscoverSerialPortsAsync()
    {
        var printers = new List<Printer>();

        try
        {
            foreach (var config in _settings.SerialPorts.Where(p => p.Enabled))
            {
                var portExists = SerialPort.GetPortNames().Contains(config.PortName, StringComparer.OrdinalIgnoreCase);
                printers.Add(new Printer
                {
                    Id = $"serial_{config.PortName.ToLowerInvariant()}",
                    Name = config.Name,
                    ConnectionType = PrinterConnectionType.USB,
                    DevicePath = config.PortName,
                    Status = portExists ? PrinterStatus.Online : PrinterStatus.Offline,
                    LastSeen = DateTime.UtcNow
                });
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering serial port printers");
        }

        return printers;
    }

    private bool IsThermalPrinter(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var pattern in ThermalPrinterPatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var pattern in _settings.PrinterNamePatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string SanitizeId(string name)
    {
        return string.Join("_", name.Split(Path.GetInvalidFileNameChars()))
            .Replace(" ", "_")
            .Replace(".", "_")
            .ToLowerInvariant();
    }

    public Task<bool> IsAccessibleAsync(string devicePath)
    {
        try
        {
            // Check if it's a LibUSB device (format: "VID:PID" e.g., "0483:5720")
            if (devicePath.Contains(':') && devicePath.Length <= 9)
            {
                var parts = devicePath.Split(':');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out int vid) &&
                    int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out int pid))
                {
                    return Task.FromResult(_libUsbManager.IsDeviceAccessible(vid, pid));
                }
            }

            // Check if it's a serial port
            if (devicePath.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(SerialPort.GetPortNames()
                    .Contains(devicePath, StringComparer.OrdinalIgnoreCase));
            }

            // Otherwise it's a Windows printer name
            return Task.FromResult(WindowsPrinterManager.IsPrinterAccessible(devicePath));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking accessibility for: {DevicePath}", devicePath);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> SendDataAsync(string devicePath, byte[] data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Sending {ByteCount} bytes to device: {DevicePath}", data.Length, devicePath);

            // 1. Check if it's a LibUSB device (format: "VID:PID")
            if (devicePath.Contains(':') && devicePath.Length <= 9)
            {
                var parts = devicePath.Split(':');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out int vid) &&
                    int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out int pid))
                {
                    _logger.LogDebug("Sending via LibUSB to {VID:X4}:{PID:X4}", vid, pid);
                    return await _libUsbManager.SendDataAsync(vid, pid, data, cancellationToken);
                }
            }

            // 2. Check if it's a serial port
            if (devicePath.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                return await SendToSerialPortAsync(devicePath, data, cancellationToken);
            }

            // 3. Otherwise send via Windows spooler
            _logger.LogDebug("Sending via Windows Spooler to {PrinterName}", devicePath);
            var success = await WindowsPrinterManager.SendBytesToPrinterAsync(devicePath, data);

            if (success)
            {
                _logger.LogDebug("Successfully sent data to printer: {DevicePath}", devicePath);
            }
            else
            {
                _logger.LogWarning("Failed to send data to printer: {DevicePath}", devicePath);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to device: {DevicePath}", devicePath);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> SendToSerialPortAsync(string portName, byte[] data, CancellationToken cancellationToken)
    {
        var config = _settings.SerialPorts.FirstOrDefault(p =>
            p.PortName.Equals(portName, StringComparison.OrdinalIgnoreCase));

        int baudRate = config?.BaudRate ?? 9600;
        var parity = Enum.TryParse<Parity>(config?.Parity ?? "None", true, out var p) ? p : Parity.None;
        int dataBits = config?.DataBits ?? 8;
        var stopBits = Enum.TryParse<StopBits>(config?.StopBits ?? "One", true, out var s) ? s : StopBits.One;

        try
        {
            using var port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                WriteTimeout = _settings.Connection.PrintTimeoutMs,
                ReadTimeout = _settings.Connection.TimeoutMs
            };

            port.Open();
            await port.BaseStream.WriteAsync(data, cancellationToken);
            await port.BaseStream.FlushAsync(cancellationToken);

            _logger.LogDebug("Successfully sent {ByteCount} bytes to serial port: {PortName}", data.Length, portName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to serial port: {PortName}", portName);
            throw;
        }
    }
}
