using System.Text;
using OpenPrint.Models;

namespace OpenPrint.Services;

/// <summary>
/// Builds ESC/POS commands for thermal printers
/// </summary>
public class EscPosCommandBuilder
{
    // ESC/POS Command Constants
    private static readonly byte[] CMD_INITIALIZE = { 0x1B, 0x40 };              // ESC @ - Initialize printer
    private static readonly byte[] CMD_ALIGN_LEFT = { 0x1B, 0x61, 0x00 };        // ESC a 0
    private static readonly byte[] CMD_ALIGN_CENTER = { 0x1B, 0x61, 0x01 };      // ESC a 1
    private static readonly byte[] CMD_ALIGN_RIGHT = { 0x1B, 0x61, 0x02 };       // ESC a 2
    private static readonly byte[] CMD_BOLD_ON = { 0x1B, 0x45, 0x01 };           // ESC E 1
    private static readonly byte[] CMD_BOLD_OFF = { 0x1B, 0x45, 0x00 };          // ESC E 0
    private static readonly byte[] CMD_FONT_NORMAL = { 0x1D, 0x21, 0x00 };       // GS ! 0 - Normal size
    private static readonly byte[] CMD_FONT_DOUBLE_HEIGHT = { 0x1D, 0x21, 0x01 };// GS ! 1 - Double height
    private static readonly byte[] CMD_FONT_DOUBLE_WIDTH = { 0x1D, 0x21, 0x10 }; // GS ! 16 - Double width
    private static readonly byte[] CMD_FONT_LARGE = { 0x1D, 0x21, 0x11 };        // GS ! 17 - Double width+height
    private static readonly byte[] CMD_FONT_SMALL = { 0x1B, 0x4D, 0x01 };        // ESC M 1 - Small font
    private static readonly byte[] CMD_FONT_SMALL_OFF = { 0x1B, 0x4D, 0x00 };    // ESC M 0 - Normal font
    private static readonly byte[] CMD_CUT_PAPER = { 0x1D, 0x56, 0x42, 0x00 };   // GS V 66 0 - Partial cut
    private static readonly byte[] CMD_CUT_PAPER_FULL = { 0x1D, 0x56, 0x00 };    // GS V 0 - Full cut
    private static readonly byte[] CMD_LINE_FEED = { 0x0A };                      // LF
    private static readonly byte[] CMD_CARRIAGE_RETURN = { 0x0D };               // CR
    private static readonly byte[] CMD_FEED_LINES_3 = { 0x1B, 0x64, 0x03 };      // ESC d 3 - Feed 3 lines

    // Codepage selection for Cyrillic
    private static readonly byte[] CMD_CODEPAGE_CP866 = { 0x1B, 0x74, 0x11 };    // ESC t 17 - CP866
    private static readonly byte[] CMD_CODEPAGE_WIN1251 = { 0x1B, 0x74, 0x2E };  // ESC t 46 - Windows-1251

    private readonly List<byte> _buffer = new();
    private readonly Encoding _encoding;
    private readonly PrintDefaultsConfig _defaults;

    static EscPosCommandBuilder()
    {
        // Register codepage provider for CP866 and other legacy encodings
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public EscPosCommandBuilder(PrintDefaultsConfig? defaults = null)
    {
        _defaults = defaults ?? new PrintDefaultsConfig();
        _encoding = GetEncoding(_defaults.Encoding);
    }

    public static Encoding GetEncoding(string? encodingName)
    {
        return encodingName?.ToUpperInvariant() switch
        {
            "CP866" => Encoding.GetEncoding(866),
            "WINDOWS-1251" or "WIN1251" or "1251" => Encoding.GetEncoding(1251),
            "UTF-8" or "UTF8" => Encoding.UTF8,
            _ => Encoding.GetEncoding(866) // Default to CP866
        };
    }

    /// <summary>
    /// Initialize printer and reset settings
    /// </summary>
    public EscPosCommandBuilder Initialize()
    {
        _buffer.AddRange(CMD_INITIALIZE);
        return this;
    }

    /// <summary>
    /// Set codepage for Cyrillic support
    /// </summary>
    public EscPosCommandBuilder SetCodepage(string? encoding = null)
    {
        var enc = encoding?.ToUpperInvariant() ?? _defaults.Encoding.ToUpperInvariant();

        if (enc == "CP866")
        {
            _buffer.AddRange(CMD_CODEPAGE_CP866);
        }
        else if (enc is "WINDOWS-1251" or "WIN1251" or "1251")
        {
            _buffer.AddRange(CMD_CODEPAGE_WIN1251);
        }
        // UTF-8 doesn't need codepage selection on most modern printers

        return this;
    }

    /// <summary>
    /// Set text alignment
    /// </summary>
    public EscPosCommandBuilder SetAlignment(string? alignment = null)
    {
        var align = alignment?.ToLowerInvariant() ?? _defaults.Alignment.ToLowerInvariant();

        _buffer.AddRange(align switch
        {
            "center" => CMD_ALIGN_CENTER,
            "right" => CMD_ALIGN_RIGHT,
            _ => CMD_ALIGN_LEFT
        });

        return this;
    }

    /// <summary>
    /// Set font size
    /// </summary>
    public EscPosCommandBuilder SetFontSize(string? fontSize = null)
    {
        var size = fontSize?.ToLowerInvariant() ?? _defaults.FontSize.ToLowerInvariant();

        _buffer.AddRange(size switch
        {
            "small" => CMD_FONT_SMALL,
            "large" => CMD_FONT_LARGE,
            _ => CMD_FONT_NORMAL
        });

        // Reset small font if not using small
        if (size != "small")
        {
            _buffer.AddRange(CMD_FONT_SMALL_OFF);
        }

        return this;
    }

    /// <summary>
    /// Set bold text mode
    /// </summary>
    public EscPosCommandBuilder SetBold(bool bold)
    {
        _buffer.AddRange(bold ? CMD_BOLD_ON : CMD_BOLD_OFF);
        return this;
    }

    /// <summary>
    /// Add text content with current encoding
    /// </summary>
    public EscPosCommandBuilder AddText(string text, string? encoding = null)
    {
        var enc = encoding != null ? GetEncoding(encoding) : _encoding;
        var bytes = enc.GetBytes(text);
        _buffer.AddRange(bytes);
        return this;
    }

    /// <summary>
    /// Add line feed
    /// </summary>
    public EscPosCommandBuilder AddLineFeed(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            _buffer.AddRange(CMD_LINE_FEED);
        }
        return this;
    }

    /// <summary>
    /// Add text followed by line feed
    /// </summary>
    public EscPosCommandBuilder AddLine(string text, string? encoding = null)
    {
        AddText(text, encoding);
        AddLineFeed();
        return this;
    }

    /// <summary>
    /// Feed paper by specified number of lines
    /// </summary>
    public EscPosCommandBuilder FeedLines(int lines = 3)
    {
        _buffer.Add(0x1B);
        _buffer.Add(0x64);
        _buffer.Add((byte)Math.Min(lines, 255));
        return this;
    }

    /// <summary>
    /// Cut paper (partial cut)
    /// </summary>
    public EscPosCommandBuilder CutPaper(bool fullCut = false)
    {
        FeedLines(3); // Feed paper before cutting
        _buffer.AddRange(fullCut ? CMD_CUT_PAPER_FULL : CMD_CUT_PAPER);
        return this;
    }

    /// <summary>
    /// Add separator line
    /// </summary>
    public EscPosCommandBuilder AddSeparator(char character = '=', int width = 32)
    {
        AddLine(new string(character, width));
        return this;
    }

    /// <summary>
    /// Build complete receipt from text content with options
    /// </summary>
    public static byte[] BuildReceipt(string content, PrintOptions? options, PrintDefaultsConfig? defaults = null)
    {
        var builder = new EscPosCommandBuilder(defaults);
        var effectiveDefaults = defaults ?? new PrintDefaultsConfig();

        builder
            .Initialize()
            .SetCodepage(options?.Encoding ?? effectiveDefaults.Encoding)
            .SetAlignment(options?.Alignment ?? effectiveDefaults.Alignment)
            .SetFontSize(options?.FontSize ?? effectiveDefaults.FontSize);

        if (options?.Bold == true)
        {
            builder.SetBold(true);
        }

        // Process content line by line
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            builder.AddLine(line, options?.Encoding ?? effectiveDefaults.Encoding);
        }

        // Reset bold if was enabled
        if (options?.Bold == true)
        {
            builder.SetBold(false);
        }

        // Cut paper if enabled
        var shouldCut = options?.CutPaper ?? effectiveDefaults.PaperCut;
        if (shouldCut)
        {
            builder.CutPaper();
        }

        return builder.Build();
    }

    /// <summary>
    /// Build test page content
    /// </summary>
    public static byte[] BuildTestPage(string printerName)
    {
        var builder = new EscPosCommandBuilder();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        builder
            .Initialize()
            .SetCodepage("CP866")
            .SetAlignment("center")
            .AddSeparator('=', 32)
            .SetFontSize("large")
            .AddLine("OPENPRINT TEST")
            .SetFontSize("normal")
            .AddSeparator('=', 32)
            .AddLineFeed()
            .SetAlignment("left")
            .AddLine($"Printer: {printerName}")
            .AddLine($"Time: {timestamp}")
            .AddLine("Status: OK")
            .AddLineFeed()
            .SetAlignment("center")
            .AddLine("Hello World!")
            .AddLine("Привет Мир!")
            .AddLineFeed()
            .AddSeparator('=', 32)
            .CutPaper();

        return builder.Build();
    }

    /// <summary>
    /// Get the built byte array
    /// </summary>
    public byte[] Build()
    {
        return _buffer.ToArray();
    }

    /// <summary>
    /// Clear the buffer
    /// </summary>
    public EscPosCommandBuilder Clear()
    {
        _buffer.Clear();
        return this;
    }
}
