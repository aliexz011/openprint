using System.Collections.Concurrent;
using OpenPrint.Models;
using Microsoft.Extensions.Options;

namespace OpenPrint.Services;

/// <summary>
/// Thread-safe print queue for managing concurrent print jobs
/// </summary>
public class PrintQueue
{
    private readonly ILogger<PrintQueue> _logger;
    private readonly OpenPrintSettings _settings;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _printerLocks = new();
    private readonly ConcurrentQueue<PrintJob> _pendingJobs = new();
    private long _totalJobsProcessed;
    private long _totalJobsFailed;

    public PrintQueue(ILogger<PrintQueue> logger, IOptions<OpenPrintSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Get or create a lock for a specific printer
    /// </summary>
    public SemaphoreSlim GetPrinterLock(string printerId)
    {
        return _printerLocks.GetOrAdd(printerId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Execute a print job with printer-level locking
    /// </summary>
    public async Task<PrintResponse> ExecuteAsync(
        string printerId,
        string printerName,
        Func<CancellationToken, Task<bool>> printAction,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var job = new PrintJob
        {
            Id = jobId,
            PrinterId = printerId,
            PrinterName = printerName,
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Print job {JobId} queued for printer {PrinterName}", jobId, printerName);

        var printerLock = GetPrinterLock(printerId);

        // Try to acquire the lock with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_settings.Connection.PrintTimeoutMs);

        bool lockAcquired = false;
        try
        {
            lockAcquired = await printerLock.WaitAsync(_settings.Connection.PrintTimeoutMs, timeoutCts.Token);

            if (!lockAcquired)
            {
                _logger.LogWarning("Print job {JobId} timed out waiting for printer {PrinterName}", jobId, printerName);
                Interlocked.Increment(ref _totalJobsFailed);
                return PrintResponse.Fail("Printer is busy. Please try again later.", "Timeout waiting for printer");
            }

            job.StartedAt = DateTime.UtcNow;
            _logger.LogDebug("Print job {JobId} started on printer {PrinterName}", jobId, printerName);

            var success = await printAction(timeoutCts.Token);

            job.CompletedAt = DateTime.UtcNow;

            if (success)
            {
                Interlocked.Increment(ref _totalJobsProcessed);
                _logger.LogInformation("Print job {JobId} completed successfully on {PrinterName} in {Duration}ms",
                    jobId, printerName, (job.CompletedAt.Value - job.StartedAt.Value).TotalMilliseconds);

                return PrintResponse.Ok("Print successful", printerName);
            }
            else
            {
                Interlocked.Increment(ref _totalJobsFailed);
                _logger.LogWarning("Print job {JobId} failed on {PrinterName}", jobId, printerName);
                return PrintResponse.Fail("Print operation failed", "Unknown error during printing");
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _totalJobsFailed);
            _logger.LogWarning("Print job {JobId} timed out during execution on {PrinterName}", jobId, printerName);
            return PrintResponse.Fail("Print operation timed out", "Operation exceeded timeout limit");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalJobsFailed);
            _logger.LogError(ex, "Print job {JobId} failed with exception on {PrinterName}", jobId, printerName);
            return PrintResponse.Fail("Print operation failed", ex.Message);
        }
        finally
        {
            if (lockAcquired)
            {
                printerLock.Release();
            }
        }
    }

    public long TotalJobsProcessed => Interlocked.Read(ref _totalJobsProcessed);
    public long TotalJobsFailed => Interlocked.Read(ref _totalJobsFailed);
}

public class PrintJob
{
    public string Id { get; set; } = string.Empty;
    public string PrinterId { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
}

public enum PrintJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
