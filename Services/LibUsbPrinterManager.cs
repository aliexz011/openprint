using LibUsbDotNet;
using LibUsbDotNet.Main;
using OpenPrint.Models;

namespace OpenPrint.Services;

/// <summary>
/// Direct USB printer access using libusb - works without printer drivers!
/// This allows sending raw ESC/POS data directly to USB thermal printers.
/// </summary>
public class LibUsbPrinterManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    // Common USB Vendor IDs for thermal/POS printers
    private static readonly Dictionary<int, string> KnownVendors = new()
    {
        { 0x0483, "STMicroelectronics" },
        { 0x0416, "Winbond" },
        { 0x04B8, "Epson" },
        { 0x04F9, "Brother" },
        { 0x0519, "Star Micronics" },
        { 0x067B, "Prolific" },
        { 0x1008, "Gprinter" },
        { 0x1504, "Epson" },
        { 0x154F, "SNBC" },
        { 0x1659, "ShenZhen" },
        { 0x1A86, "QinHeng (CH340)" },
        { 0x20D1, "Xprinter" },
        { 0x2730, "Citizen" },
        { 0x28E9, "GD32" },
        { 0x4348, "WCH (CH341)" },
        { 0x6868, "Rongta" },
        { 0x0DD4, "Bixolon" },
        { 0x2207, "Rockchip" },
    };

    // USB Class codes for printers
    private const byte USB_CLASS_PRINTER = 0x07;
    private const byte USB_CLASS_CDC = 0x02;
    private const byte USB_CLASS_VENDOR = 0xFF;

    public LibUsbPrinterManager(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discover all USB devices that could be thermal printers
    /// </summary>
    public IEnumerable<LibUsbPrinterInfo> DiscoverPrinters()
    {
        var printers = new List<LibUsbPrinterInfo>();

        try
        {
            // Get all connected USB devices
            var allDevices = UsbDevice.AllDevices;

            foreach (UsbRegistry registry in allDevices)
            {
                try
                {
                    var vendorId = registry.Vid;
                    var productId = registry.Pid;

                    bool isKnownVendor = KnownVendors.ContainsKey(vendorId);
                    string? productName = null;
                    string? manufacturer = null;

                    // Try to get device info
                    try
                    {
                        if (registry.Open(out UsbDevice device))
                        {
                            try
                            {
                                productName = device.Info?.ProductString;
                                manufacturer = device.Info?.ManufacturerString;
                            }
                            catch { }
                            finally
                            {
                                device?.Close();
                            }
                        }
                    }
                    catch { }

                    productName ??= $"USB Device {vendorId:X4}:{productId:X4}";
                    var vendorName = KnownVendors.TryGetValue(vendorId, out var vn) ? vn : manufacturer ?? "Unknown";

                    // Check if product name suggests it's a printer
                    bool looksLikePrinter =
                        productName.Contains("printer", StringComparison.OrdinalIgnoreCase) ||
                        productName.Contains("POS", StringComparison.OrdinalIgnoreCase) ||
                        productName.Contains("thermal", StringComparison.OrdinalIgnoreCase) ||
                        productName.Contains("receipt", StringComparison.OrdinalIgnoreCase) ||
                        productName.Contains("XP-", StringComparison.OrdinalIgnoreCase) ||
                        productName.Contains("58", StringComparison.OrdinalIgnoreCase) ||
                        productName.Contains("80", StringComparison.OrdinalIgnoreCase) ||
                        isKnownVendor;

                    if (looksLikePrinter)
                    {
                        printers.Add(new LibUsbPrinterInfo
                        {
                            VendorId = vendorId,
                            ProductId = productId,
                            VendorName = vendorName,
                            ProductName = productName,
                            Manufacturer = manufacturer,
                            DeviceId = $"usb_{vendorId:X4}_{productId:X4}",
                            IsPrinterClass = true
                        });

                        _logger.LogDebug("Found USB device: {Vendor}:{Product} - {Name}",
                            vendorId.ToString("X4"), productId.ToString("X4"), productName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error examining USB device");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering USB devices via libusb. Make sure WinUSB driver is installed (use Zadig).");
        }

        _logger.LogInformation("LibUsb discovered {Count} potential printer devices", printers.Count);
        return printers;
    }

    /// <summary>
    /// Send raw data to a USB printer
    /// </summary>
    public async Task<bool> SendDataAsync(int vendorId, int productId, byte[] data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Sending {ByteCount} bytes to USB device {VendorId:X4}:{ProductId:X4}",
                data.Length, vendorId, productId);

            var finder = new UsbDeviceFinder(vendorId, productId);
            var device = UsbDevice.OpenUsbDevice(finder);

            if (device == null)
            {
                _logger.LogError("USB device {VendorId:X4}:{ProductId:X4} not found or cannot be opened. Install WinUSB driver using Zadig.",
                    vendorId, productId);
                return false;
            }

            try
            {
                // If this is a "whole" USB device (libusb-win32, WinUSB), set configuration
                if (device is IUsbDevice wholeUsbDevice)
                {
                    wholeUsbDevice.SetConfiguration(1);
                    wholeUsbDevice.ClaimInterface(0);
                }

                // Find bulk OUT endpoint - usually endpoint 1 or 2
                var writer = device.OpenEndpointWriter(WriteEndpointID.Ep01);

                // Try to write
                var errorCode = writer.Write(data, 5000, out int bytesWritten);

                if (errorCode != ErrorCode.None)
                {
                    // Try endpoint 2
                    writer = device.OpenEndpointWriter(WriteEndpointID.Ep02);
                    errorCode = writer.Write(data, 5000, out bytesWritten);
                }

                if (errorCode != ErrorCode.None)
                {
                    _logger.LogError("USB write error: {Error}", errorCode);
                    return false;
                }

                _logger.LogDebug("Successfully sent {BytesWritten} bytes to USB device", bytesWritten);
                return bytesWritten == data.Length;
            }
            finally
            {
                if (device is IUsbDevice wholeUsbDevice)
                {
                    wholeUsbDevice.ReleaseInterface(0);
                }
                device.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to USB device {VendorId:X4}:{ProductId:X4}", vendorId, productId);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Check if a USB device is accessible
    /// </summary>
    public bool IsDeviceAccessible(int vendorId, int productId)
    {
        try
        {
            var finder = new UsbDeviceFinder(vendorId, productId);
            var device = UsbDevice.OpenUsbDevice(finder);

            if (device == null)
                return false;

            device.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();

        // Cleanup LibUsb
        UsbDevice.Exit();
    }
}

/// <summary>
/// Information about a USB printer discovered via libusb
/// </summary>
public class LibUsbPrinterInfo
{
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public bool IsPrinterClass { get; set; }

    /// <summary>
    /// Get device path in format "VID:PID" for configuration
    /// </summary>
    public string GetDevicePath() => $"{VendorId:X4}:{ProductId:X4}";
}
