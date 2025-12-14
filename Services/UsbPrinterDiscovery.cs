using OpenPrint.Models;
using Microsoft.Extensions.Options;

namespace OpenPrint.Services;

/// <summary>
/// Discovers and manages USB-connected thermal printers on Linux
/// </summary>
public class UsbPrinterDiscovery : IUsbPrinterDiscovery
{
    private readonly ILogger<UsbPrinterDiscovery> _logger;
    private readonly OpenPrintSettings _settings;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public UsbPrinterDiscovery(ILogger<UsbPrinterDiscovery> logger, IOptions<OpenPrintSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IEnumerable<Printer>> DiscoverAsync()
    {
        var printers = new List<Printer>();

        if (!_settings.AutoDiscoverUSB)
        {
            _logger.LogDebug("USB auto-discovery is disabled");
            return printers;
        }

        foreach (var pattern in _settings.UsbDevicePaths)
        {
            var discovered = await DiscoverByPatternAsync(pattern);
            printers.AddRange(discovered);
        }

        _logger.LogInformation("Discovered {Count} USB printers", printers.Count);
        return printers;
    }

    private async Task<IEnumerable<Printer>> DiscoverByPatternAsync(string pattern)
    {
        var printers = new List<Printer>();

        try
        {
            // Convert glob pattern to directory and search pattern
            var directory = Path.GetDirectoryName(pattern) ?? "/dev";
            var searchPattern = Path.GetFileName(pattern);

            if (!Directory.Exists(directory))
            {
                _logger.LogDebug("Directory does not exist: {Directory}", directory);
                return printers;
            }

            // Handle glob patterns
            var files = searchPattern.Contains('*')
                ? Directory.GetFiles(directory, searchPattern.Replace("*", "*"))
                : new[] { pattern };

            // For /dev/usb/lp* pattern
            if (pattern.Contains("/dev/usb/lp"))
            {
                if (Directory.Exists("/dev/usb"))
                {
                    files = Directory.GetFiles("/dev/usb")
                        .Where(f => Path.GetFileName(f).StartsWith("lp"))
                        .ToArray();
                }
            }
            // For /dev/ttyUSB* pattern
            else if (pattern.Contains("/dev/ttyUSB"))
            {
                files = Directory.GetFiles("/dev")
                    .Where(f => Path.GetFileName(f).StartsWith("ttyUSB"))
                    .ToArray();
            }

            foreach (var devicePath in files)
            {
                var accessible = await IsAccessibleAsync(devicePath);
                var printer = new Printer
                {
                    Id = $"usb_{Path.GetFileName(devicePath)}",
                    Name = $"USB Printer ({Path.GetFileName(devicePath)})",
                    ConnectionType = PrinterConnectionType.USB,
                    DevicePath = devicePath,
                    Status = accessible ? PrinterStatus.Online : PrinterStatus.Offline,
                    LastSeen = DateTime.UtcNow
                };

                printers.Add(printer);
                _logger.LogDebug("Found USB printer: {DevicePath}, Status: {Status}", devicePath, printer.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering USB printers with pattern: {Pattern}", pattern);
        }

        return printers;
    }

    public Task<bool> IsAccessibleAsync(string devicePath)
    {
        try
        {
            // Check if device file exists and is accessible
            var fileInfo = new FileInfo(devicePath);
            if (!fileInfo.Exists)
            {
                return Task.FromResult(false);
            }

            // Try to open for writing to check accessibility
            using var fs = new FileStream(devicePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return Task.FromResult(true);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Permission denied for device: {DevicePath}. Add user to 'lp' or 'dialout' group.", devicePath);
            return Task.FromResult(false);
        }
        catch (IOException ex)
        {
            _logger.LogDebug("Device not accessible: {DevicePath} - {Message}", devicePath, ex.Message);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking device accessibility: {DevicePath}", devicePath);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> SendDataAsync(string devicePath, byte[] data, CancellationToken cancellationToken = default)
    {
        // Ensure only one write operation at a time per device
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Sending {ByteCount} bytes to USB device: {DevicePath}", data.Length, devicePath);

            await using var fs = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await fs.WriteAsync(data, cancellationToken);
            await fs.FlushAsync(cancellationToken);

            _logger.LogDebug("Successfully sent data to USB device: {DevicePath}", devicePath);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission denied writing to device: {DevicePath}", devicePath);
            throw new InvalidOperationException($"Permission denied for device {devicePath}. Ensure the user is in the 'lp' or 'dialout' group.", ex);
        }
        catch (IOException ex) when (ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Device is busy: {DevicePath}", devicePath);
            throw new InvalidOperationException($"Device {devicePath} is busy. Try again later.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to USB device: {DevicePath}", devicePath);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
