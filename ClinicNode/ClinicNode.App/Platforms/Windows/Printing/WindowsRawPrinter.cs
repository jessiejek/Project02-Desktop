//JJQR - AI (Nov 21, 2026) - send raw ESC/POS bytes to a USB/serial thermal printer through the Windows spooler (datatype RAW).
using System.Runtime.InteropServices;

namespace ClinicNode.App.WinUI.Printing;

public static class WindowsRawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DOC_INFO_1
    {
        public string pDocName;
        public string? pOutputFile;
        public string pDatatype;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 di);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndDocPrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool StartPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

    public static void Send(string printerName, string jobName, byte[] payload)
    {
        if (!OpenPrinter(printerName, out var h, IntPtr.Zero))
            throw new InvalidOperationException($"Printer \"{printerName}\" was not found (error {Marshal.GetLastWin32Error()}).");
        try
        {
            var di = new DOC_INFO_1 { pDocName = jobName, pDatatype = "RAW" };
            if (StartDocPrinter(h, 1, ref di) == 0) throw new InvalidOperationException($"StartDocPrinter failed ({Marshal.GetLastWin32Error()}).");
            try
            {
                StartPagePrinter(h);
                if (!WritePrinter(h, payload, payload.Length, out var written) || written != payload.Length)
                    throw new InvalidOperationException($"WritePrinter failed ({Marshal.GetLastWin32Error()}).");
                EndPagePrinter(h);
            }
            finally
            {
                EndDocPrinter(h);
            }
        }
        finally
        {
            ClosePrinter(h);
        }
    }
}
//JJQR - END
