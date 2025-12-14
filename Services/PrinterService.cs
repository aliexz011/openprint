using OpenPrint.Models;
using Microsoft.Extensions.Options;

namespace OpenPrint.Services;

/// <summary>
/// Main printer service orchestrating USB and network printers
/// </summary>
public class PrinterService : IPrinterService, IDisposable
{
    private readonly ILogger<PrinterService> _logger;
    private readonly IUsbPrinterDiscovery _usbDiscovery;
    private readonly INetworkPrinterManager _networkManager;
    private readonly PrintQueue _printQueue;
    private readonly OpenPrintSettings _settings;

    private List<Printer> _cachedPrinters = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;

    public PrinterService(
        ILogger<PrinterService> logger,
        IUsbPrinterDiscovery usbDiscovery,
        INetworkPrinterManager networkManager,
        PrintQueue printQueue,
        IOptions<OpenPrintSettings> settings)
    {
        _logger = logger;
        _usbDiscovery = usbDiscovery;
        _networkManager = networkManager;
        _printQueue = printQueue;
        _settings = settings.Value;

        // Start background refresh timer
        var interval = TimeSpan.FromSeconds(_settings.Discovery.CacheRefreshIntervalSeconds);
        _refreshTimer = new Timer(async _ => await RefreshPrintersAsync(), null, TimeSpan.Zero, interval);

        _logger.LogInformation("PrinterService initialized with {RefreshInterval}s refresh interval",
            _settings.Discovery.CacheRefreshIntervalSeconds);
    }

    public async Task<IReadOnlyList<Printer>> GetPrintersAsync()
    {
        // Return cached list if still valid
        var cacheAge = DateTime.UtcNow - _lastRefresh;
        if (_cachedPrinters.Any() && cacheAge.TotalSeconds < _settings.Discovery.CacheRefreshIntervalSeconds)
        {
            return _cachedPrinters.AsReadOnly();
        }

        await RefreshPrintersAsync();
        return _cachedPrinters.AsReadOnly();
    }

    public async Task<Printer?> GetPrinterAsync(string identifier)
    {
        var printers = await GetPrintersAsync();

        // Try exact match first
        var printer = printers.FirstOrDefault(p =>
            p.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
            (p.DevicePath != null && p.DevicePath.Equals(identifier, StringComparison.OrdinalIgnoreCase)) ||
            (p.IpAddress != null && p.IpAddress.Equals(identifier, StringComparison.OrdinalIgnoreCase)));

        if (printer != null) return printer;

        // Try partial match
        return printers.FirstOrDefault(p =>
            p.Id.Contains(identifier, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains(identifier, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshPrintersAsync()
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            _logger.LogDebug("Skipping refresh - already in progress");
            return;
        }

        try
        {
            _logger.LogDebug("Refreshing printer list...");

            var allPrinters = new List<Printer>();

            // Discover USB printers
            var usbPrinters = await _usbDiscovery.DiscoverAsync();
            allPrinters.AddRange(usbPrinters);

            // Get network printers
            var networkPrinters = await _networkManager.GetPrintersAsync();
            allPrinters.AddRange(networkPrinters);

            _cachedPrinters = allPrinters;
            _lastRefresh = DateTime.UtcNow;

            _logger.LogInformation("Printer refresh complete. Found {UsbCount} USB and {NetworkCount} network printers",
                usbPrinters.Count(), networkPrinters.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing printer list");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<PrintResponse> PrintAsync(PrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return PrintResponse.Fail("Content is required");
        }

        // Find target printer
        Printer? printer;
        if (string.IsNullOrWhiteSpace(request.PrinterIdentifier))
        {
            // Use first available online printer
            var printers = await GetPrintersAsync();
            printer = printers.FirstOrDefault(p => p.Status == PrinterStatus.Online);

            if (printer == null)
            {
                return PrintResponse.Fail("No available printers found");
            }
        }
        else
        {
            printer = await GetPrinterAsync(request.PrinterIdentifier);
            if (printer == null)
            {
                return PrintResponse.Fail($"Printer not found: {request.PrinterIdentifier}");
            }
        }

        // Check printer status
        if (printer.Status == PrinterStatus.Offline)
        {
            return PrintResponse.Fail($"Printer is offline: {printer.Name}");
        }

        // Build ESC/POS commands
        var escPosData = EscPosCommandBuilder.BuildReceipt(
            request.Content,
            request.Options,
            _settings.PrintDefaults);

        _logger.LogDebug("Built {ByteCount} bytes of ESC/POS commands for printer {PrinterName}",
            escPosData.Length, printer.Name);

        // Execute print via queue
        return await _printQueue.ExecuteAsync(
            printer.Id,
            printer.Name,
            async ct => await SendToPrinterAsync(printer, escPosData, ct));
    }

    public async Task<PrintResponse> PrintTestPageAsync(string? printerIdentifier = null)
    {
        // Find target printer
        Printer? printer;
        if (string.IsNullOrWhiteSpace(printerIdentifier))
        {
            var printers = await GetPrintersAsync();
            printer = printers.FirstOrDefault(p => p.Status == PrinterStatus.Online);

            if (printer == null)
            {
                return PrintResponse.Fail("No available printers found");
            }
        }
        else
        {
            printer = await GetPrinterAsync(printerIdentifier);
            if (printer == null)
            {
                return PrintResponse.Fail($"Printer not found: {printerIdentifier}");
            }
        }

        // Build test page
        var testPageData = EscPosCommandBuilder.BuildTestPage(printer.Name);

        _logger.LogInformation("Printing test page to {PrinterName}", printer.Name);

        // Execute print via queue
        return await _printQueue.ExecuteAsync(
            printer.Id,
            printer.Name,
            async ct => await SendToPrinterAsync(printer, testPageData, ct));
    }

    private async Task<bool> SendToPrinterAsync(Printer printer, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            if (printer.ConnectionType == PrinterConnectionType.USB)
            {
                if (string.IsNullOrEmpty(printer.DevicePath))
                {
                    _logger.LogError("USB printer {Name} has no device path", printer.Name);
                    return false;
                }

                return await _usbDiscovery.SendDataAsync(printer.DevicePath, data, cancellationToken);
            }
            else if (printer.ConnectionType == PrinterConnectionType.LAN)
            {
                if (string.IsNullOrEmpty(printer.IpAddress))
                {
                    _logger.LogError("Network printer {Name} has no IP address", printer.Name);
                    return false;
                }

                return await _networkManager.SendDataAsync(printer.IpAddress, printer.Port, data, cancellationToken);
            }

            _logger.LogError("Unknown connection type for printer {Name}: {Type}", printer.Name, printer.ConnectionType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to printer {Name}", printer.Name);
            return false;
        }
    }

    public int GetAvailablePrinterCount()
    {
        return _cachedPrinters.Count(p => p.Status == PrinterStatus.Online);
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshLock?.Dispose();
    }
}
