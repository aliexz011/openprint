using OpenPrint.Models;

namespace OpenPrint.Services;

/// <summary>
/// Interface for printer service operations
/// </summary>
public interface IPrinterService
{
    /// <summary>
    /// Get list of all available printers
    /// </summary>
    Task<IReadOnlyList<Printer>> GetPrintersAsync();

    /// <summary>
    /// Get a specific printer by identifier
    /// </summary>
    Task<Printer?> GetPrinterAsync(string identifier);

    /// <summary>
    /// Print content to a specific printer or first available
    /// </summary>
    Task<PrintResponse> PrintAsync(PrintRequest request);

    /// <summary>
    /// Print test page to a specific printer
    /// </summary>
    Task<PrintResponse> PrintTestPageAsync(string? printerIdentifier = null);

    /// <summary>
    /// Refresh printer discovery cache
    /// </summary>
    Task RefreshPrintersAsync();

    /// <summary>
    /// Get count of available printers
    /// </summary>
    int GetAvailablePrinterCount();
}

/// <summary>
/// Interface for USB printer discovery
/// </summary>
public interface IUsbPrinterDiscovery
{
    /// <summary>
    /// Discover USB-connected printers
    /// </summary>
    Task<IEnumerable<Printer>> DiscoverAsync();

    /// <summary>
    /// Check if a USB printer is accessible
    /// </summary>
    Task<bool> IsAccessibleAsync(string devicePath);

    /// <summary>
    /// Send data to USB printer
    /// </summary>
    Task<bool> SendDataAsync(string devicePath, byte[] data, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for network printer management
/// </summary>
public interface INetworkPrinterManager
{
    /// <summary>
    /// Get configured network printers
    /// </summary>
    Task<IEnumerable<Printer>> GetPrintersAsync();

    /// <summary>
    /// Check if a network printer is online
    /// </summary>
    Task<bool> IsOnlineAsync(string ipAddress, int port);

    /// <summary>
    /// Send data to network printer
    /// </summary>
    Task<bool> SendDataAsync(string ipAddress, int port, byte[] data, CancellationToken cancellationToken = default);
}
