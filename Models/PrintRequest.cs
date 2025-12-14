using System.Text.Json.Serialization;

namespace OpenPrint.Models;

/// <summary>
/// Request model for print API
/// </summary>
public class PrintRequest
{
    /// <summary>
    /// Printer identifier (name, IP, or device path). If null, uses first available printer.
    /// </summary>
    [JsonPropertyName("printerIdentifier")]
    public string? PrinterIdentifier { get; set; }

    /// <summary>
    /// Text content to print
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Print options
    /// </summary>
    [JsonPropertyName("options")]
    public PrintOptions? Options { get; set; }
}

public class PrintOptions
{
    /// <summary>
    /// Font size: normal, small, large
    /// </summary>
    [JsonPropertyName("fontSize")]
    public string? FontSize { get; set; }

    /// <summary>
    /// Text alignment: left, center, right
    /// </summary>
    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    /// <summary>
    /// Whether to cut paper after printing
    /// </summary>
    [JsonPropertyName("cutPaper")]
    public bool? CutPaper { get; set; }

    /// <summary>
    /// Text encoding: CP866, Windows-1251, UTF-8
    /// </summary>
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    /// <summary>
    /// Enable bold text
    /// </summary>
    [JsonPropertyName("bold")]
    public bool? Bold { get; set; }
}

public enum FontSize
{
    Normal,
    Small,
    Large
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}
