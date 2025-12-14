namespace OpenPrint.Models;

/// <summary>
/// Represents a thermal printer device
/// </summary>
public class Printer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PrinterConnectionType ConnectionType { get; set; }
    public string? DevicePath { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 9100;
    public PrinterStatus Status { get; set; } = PrinterStatus.Unknown;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
}

public enum PrinterConnectionType
{
    USB,
    LAN
}

public enum PrinterStatus
{
    Online,
    Offline,
    Error,
    Busy,
    Unknown
}

/// <summary>
/// DTO for printer list response
/// </summary>
public class PrinterDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public string? DevicePath { get; set; }
    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }

    public static PrinterDto FromPrinter(Printer printer) => new()
    {
        Id = printer.Id,
        Name = printer.Name,
        ConnectionType = printer.ConnectionType.ToString(),
        DevicePath = printer.ConnectionType == PrinterConnectionType.USB ? printer.DevicePath : null,
        IpAddress = printer.ConnectionType == PrinterConnectionType.LAN ? printer.IpAddress : null,
        Port = printer.ConnectionType == PrinterConnectionType.LAN ? printer.Port : null,
        Status = printer.Status.ToString().ToLowerInvariant(),
        LastSeen = printer.LastSeen
    };
}

public class PrintersListResponse
{
    public List<PrinterDto> Printers { get; set; } = new();
    public int Count { get; set; }
}
