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
    public List<string> UsbDevicePaths { get; set; } = new() { "/dev/usb/lp*", "/dev/ttyUSB*" };
    public List<NetworkPrinterConfig> NetworkPrinters { get; set; } = new();
    public PrintDefaultsConfig PrintDefaults { get; set; } = new();
    public ConnectionConfig Connection { get; set; } = new();
    public DiscoveryConfig Discovery { get; set; } = new();
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
    public string Path { get; set; } = "/var/log/openprint/app.log";
}
