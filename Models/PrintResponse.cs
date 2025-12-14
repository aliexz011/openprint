using System.Text.Json.Serialization;

namespace OpenPrint.Models;

/// <summary>
/// Response model for print API
/// </summary>
public class PrintResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("printerUsed")]
    public string? PrinterUsed { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    public static PrintResponse Ok(string message, string? printerUsed = null) => new()
    {
        Success = true,
        Message = message,
        PrinterUsed = printerUsed,
        Timestamp = DateTime.UtcNow
    };

    public static PrintResponse Fail(string message, string? error = null) => new()
    {
        Success = false,
        Message = message,
        Error = error,
        Timestamp = DateTime.UtcNow
    };
}

/// <summary>
/// Response model for health check API
/// </summary>
public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "healthy";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = string.Empty;

    [JsonPropertyName("printersAvailable")]
    public int PrintersAvailable { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
