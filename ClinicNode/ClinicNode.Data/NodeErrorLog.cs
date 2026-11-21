// JJQR - AI (Nov 21, 2026) - append-only error log next to clinic-node.db so a live-app failure is visible without a debugger.
using System.Text;

namespace ClinicNode.Data;

public static class NodeErrorLog
{
    public const string FileName = "node-errors.log";

    public static string PathFor(NodeOptions options) => System.IO.Path.Combine(options.DataDirectory, FileName);

    public static void Write(NodeOptions options, string source, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(options.DataDirectory)) return;
        try
        {
            Directory.CreateDirectory(options.DataDirectory);
            var text = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [")
                .Append(source)
                .Append("] ")
                .AppendLine(ex.ToString())
                .AppendLine()
                .ToString();
            File.AppendAllText(PathFor(options), text);
        }
        catch
        {
            // never throw from a log write
        }
    }
}
// JJQR - END
