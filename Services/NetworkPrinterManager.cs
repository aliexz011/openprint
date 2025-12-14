using System.Net.Sockets;
using OpenPrint.Models;
using Microsoft.Extensions.Options;

namespace OpenPrint.Services;

/// <summary>
/// Manages network-connected thermal printers via TCP/IP
/// </summary>
public class NetworkPrinterManager : INetworkPrinterManager
{
    private readonly ILogger<NetworkPrinterManager> _logger;
    private readonly OpenPrintSettings _settings;

    public NetworkPrinterManager(ILogger<NetworkPrinterManager> logger, IOptions<OpenPrintSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IEnumerable<Printer>> GetPrintersAsync()
    {
        var printers = new List<Printer>();

        foreach (var config in _settings.NetworkPrinters.Where(p => p.Enabled))
        {
            var isOnline = await IsOnlineAsync(config.IpAddress, config.Port);

            var printer = new Printer
            {
                Id = $"lan_{config.IpAddress.Replace(".", "_")}_{config.Port}",
                Name = config.Name,
                ConnectionType = PrinterConnectionType.LAN,
                IpAddress = config.IpAddress,
                Port = config.Port,
                Status = isOnline ? PrinterStatus.Online : PrinterStatus.Offline,
                LastSeen = DateTime.UtcNow
            };

            printers.Add(printer);
            _logger.LogDebug("Network printer {Name} at {IP}:{Port} is {Status}",
                config.Name, config.IpAddress, config.Port, printer.Status);
        }

        _logger.LogInformation("Found {Count} configured network printers", printers.Count);
        return printers;
    }

    public async Task<bool> IsOnlineAsync(string ipAddress, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_settings.Connection.TimeoutMs));

            await client.ConnectAsync(ipAddress, port, cts.Token);
            return client.Connected;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Connection timeout checking printer at {IP}:{Port}", ipAddress, port);
            return false;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("Socket error checking printer at {IP}:{Port}: {Message}", ipAddress, port, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking printer at {IP}:{Port}", ipAddress, port);
            return false;
        }
    }

    public async Task<bool> SendDataAsync(string ipAddress, int port, byte[] data, CancellationToken cancellationToken = default)
    {
        var retryCount = _settings.Connection.RetryCount;
        var retryDelay = _settings.Connection.RetryDelayMs;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= retryCount; attempt++)
        {
            try
            {
                _logger.LogDebug("Sending {ByteCount} bytes to network printer {IP}:{Port} (attempt {Attempt}/{Max})",
                    data.Length, ipAddress, port, attempt, retryCount);

                using var client = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_settings.Connection.TimeoutMs);

                await client.ConnectAsync(ipAddress, port, cts.Token);

                if (!client.Connected)
                {
                    throw new InvalidOperationException("Failed to establish connection");
                }

                await using var stream = client.GetStream();
                stream.WriteTimeout = _settings.Connection.PrintTimeoutMs;

                await stream.WriteAsync(data, cts.Token);
                await stream.FlushAsync(cts.Token);

                _logger.LogDebug("Successfully sent data to network printer {IP}:{Port}", ipAddress, port);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Connection to {ipAddress}:{port} timed out");
                _logger.LogWarning("Timeout connecting to printer {IP}:{Port} (attempt {Attempt}/{Max})",
                    ipAddress, port, attempt, retryCount);
            }
            catch (SocketException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Socket error sending to printer {IP}:{Port} (attempt {Attempt}/{Max})",
                    ipAddress, port, attempt, retryCount);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error sending to printer {IP}:{Port} (attempt {Attempt}/{Max})",
                    ipAddress, port, attempt, retryCount);
            }

            // Wait before retry (except on last attempt)
            if (attempt < retryCount)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        _logger.LogError(lastException, "Failed to send data to printer {IP}:{Port} after {Attempts} attempts",
            ipAddress, port, retryCount);

        throw lastException ?? new InvalidOperationException($"Failed to send data to {ipAddress}:{port}");
    }
}
