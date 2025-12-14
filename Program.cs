using System.Text;
using System.Text.Json;
using OpenPrint.Models;
using OpenPrint.Services;

// Register codepage provider for CP866 and other legacy encodings
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Configure settings
builder.Services.Configure<OpenPrintSettings>(
    builder.Configuration.GetSection(OpenPrintSettings.SectionName));

// Register services
builder.Services.AddSingleton<IUsbPrinterDiscovery, UsbPrinterDiscovery>();
builder.Services.AddSingleton<INetworkPrinterManager, NetworkPrinterManager>();
builder.Services.AddSingleton<PrintQueue>();
builder.Services.AddSingleton<IPrinterService, PrinterService>();

// Configure JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

// Configure Kestrel to listen only on localhost
var settings = builder.Configuration.GetSection(OpenPrintSettings.SectionName).Get<OpenPrintSettings>()
    ?? new OpenPrintSettings();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(settings.Port);
});

// Add static files
builder.Services.AddDirectoryBrowser();

var app = builder.Build();

// Track application start time for uptime calculation
var startTime = DateTime.UtcNow;

// Serve static files (wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

// Health check endpoint
app.MapGet("/api/health", (IPrinterService printerService) =>
{
    var uptime = DateTime.UtcNow - startTime;
    var uptimeStr = uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
        : $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";

    return Results.Ok(new HealthResponse
    {
        Status = "healthy",
        Version = "1.0.0",
        Uptime = uptimeStr,
        PrintersAvailable = printerService.GetAvailablePrinterCount(),
        Timestamp = DateTime.UtcNow
    });
});

// List printers endpoint
app.MapGet("/api/printers", async (IPrinterService printerService) =>
{
    var printers = await printerService.GetPrintersAsync();

    return Results.Ok(new PrintersListResponse
    {
        Printers = printers.Select(PrinterDto.FromPrinter).ToList(),
        Count = printers.Count
    });
});

// Refresh printers endpoint
app.MapPost("/api/printers/refresh", async (IPrinterService printerService) =>
{
    await printerService.RefreshPrintersAsync();
    var printers = await printerService.GetPrintersAsync();

    return Results.Ok(new PrintersListResponse
    {
        Printers = printers.Select(PrinterDto.FromPrinter).ToList(),
        Count = printers.Count
    });
});

// Print endpoint
app.MapPost("/api/print", async (PrintRequest request, IPrinterService printerService) =>
{
    var response = await printerService.PrintAsync(request);
    return Results.Ok(response);
});

// Test print endpoint
app.MapPost("/api/print/test", async (IPrinterService printerService, string? printerId = null) =>
{
    var response = await printerService.PrintTestPageAsync(printerId);
    return Results.Ok(response);
});

// Test print for specific printer
app.MapPost("/api/printers/{printerId}/test", async (string printerId, IPrinterService printerService) =>
{
    var response = await printerService.PrintTestPageAsync(printerId);
    return Results.Ok(response);
});

// Test page route (redirect to static index.html)
app.MapGet("/test", () => Results.Redirect("/"));

// Startup logging
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("OpenPrint starting on http://{Host}:{Port}", settings.Host, settings.Port);
logger.LogInformation("Auto-discover USB: {AutoDiscoverUSB}", settings.AutoDiscoverUSB);
logger.LogInformation("Configured network printers: {Count}", settings.NetworkPrinters.Count);
logger.LogInformation("Default encoding: {Encoding}", settings.PrintDefaults.Encoding);

// Initialize printer discovery on startup
var printerService = app.Services.GetRequiredService<IPrinterService>();
await printerService.RefreshPrintersAsync();

logger.LogInformation("OpenPrint ready. Available printers: {Count}", printerService.GetAvailablePrinterCount());
logger.LogInformation("Web interface: http://{Host}:{Port}/", settings.Host, settings.Port);
logger.LogInformation("API endpoints:");
logger.LogInformation("  GET  /api/health          - Health check");
logger.LogInformation("  GET  /api/printers        - List printers");
logger.LogInformation("  POST /api/printers/refresh- Refresh printer list");
logger.LogInformation("  POST /api/print           - Print content");
logger.LogInformation("  POST /api/print/test      - Print test page");

app.Run();
