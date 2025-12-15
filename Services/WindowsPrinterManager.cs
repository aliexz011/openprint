using System.Runtime.InteropServices;
using System.ComponentModel;

namespace OpenPrint.Services;

/// <summary>
/// Windows-specific printer management using winspool.drv P/Invoke
/// Sends RAW data directly to Windows printers bypassing the print spooler driver
/// </summary>
public static class WindowsPrinterManager
{
    #region P/Invoke Declarations

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFO pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOCINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType;
    }

    #endregion

    /// <summary>
    /// Send raw bytes to a Windows printer
    /// </summary>
    /// <param name="printerName">The Windows printer name (as shown in Control Panel)</param>
    /// <param name="data">Raw ESC/POS data to send</param>
    /// <param name="documentName">Optional document name for the print job</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool SendBytesToPrinter(string printerName, byte[] data, string documentName = "OpenPrint RAW Document")
    {
        if (string.IsNullOrEmpty(printerName))
            throw new ArgumentNullException(nameof(printerName));

        if (data == null || data.Length == 0)
            throw new ArgumentNullException(nameof(data));

        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pBytes = IntPtr.Zero;

        try
        {
            // Open the printer
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"Failed to open printer '{printerName}'. Error code: {error}");
            }

            // Start a document
            var docInfo = new DOCINFO
            {
                pDocName = documentName,
                pOutputFile = null,
                pDataType = "RAW"
            };

            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"Failed to start document on printer '{printerName}'. Error code: {error}");
            }

            try
            {
                // Start a page
                if (!StartPagePrinter(hPrinter))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, $"Failed to start page on printer '{printerName}'. Error code: {error}");
                }

                try
                {
                    // Allocate unmanaged memory and copy data
                    pBytes = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, pBytes, data.Length);

                    // Write data to printer
                    if (!WritePrinter(hPrinter, pBytes, data.Length, out int bytesWritten))
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, $"Failed to write to printer '{printerName}'. Error code: {error}");
                    }

                    return bytesWritten == data.Length;
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            if (pBytes != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pBytes);

            if (hPrinter != IntPtr.Zero)
                ClosePrinter(hPrinter);
        }
    }

    /// <summary>
    /// Send raw bytes to a printer asynchronously
    /// </summary>
    public static Task<bool> SendBytesToPrinterAsync(string printerName, byte[] data, string documentName = "OpenPrint RAW Document")
    {
        return Task.Run(() => SendBytesToPrinter(printerName, data, documentName));
    }

    /// <summary>
    /// Check if a printer exists and is accessible
    /// </summary>
    public static bool IsPrinterAccessible(string printerName)
    {
        if (string.IsNullOrEmpty(printerName))
            return false;

        try
        {
            if (OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
            {
                ClosePrinter(hPrinter);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
