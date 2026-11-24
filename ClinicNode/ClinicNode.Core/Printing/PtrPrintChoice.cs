//JJQR - AI (Nov 24, 2026) - owner: "Include PTR in print", OFF by default, on the three consultation steps that print (Prescription, Lab Orders, Medical Certificate).
// The choice is kept per visit and per document on THIS computer ({DataDirectory}/ptr-print.json), so Today / chart reprints of that visit follow it too.
using System.Text.Json;

namespace ClinicNode.Core.Printing;

public interface IPtrPrintChoice
{
    bool IsOn(Guid bookingId, ClinicPrintKind kind);
    void Set(Guid bookingId, ClinicPrintKind kind, bool on);
}

public sealed class PtrPrintChoice : IPtrPrintChoice
{
    const int Keep = 3000;                       // the newest visits only: the file never grows without end
    readonly string? _path;
    readonly object _gate = new();
    List<string>? _on;                           // "bookingId|Kind", oldest first

    public PtrPrintChoice(string? dataDirectory) =>
        _path = string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, "ptr-print.json");

    static string Key(Guid bookingId, ClinicPrintKind kind) => bookingId.ToString("D") + "|" + kind;

    List<string> Load()
    {
        if (_on is not null) return _on;
        try { if (_path is not null && File.Exists(_path)) return _on = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? []; }
        catch { /* an unreadable file simply means "off" everywhere */ }
        return _on = [];
    }

    public bool IsOn(Guid bookingId, ClinicPrintKind kind) { lock (_gate) return Load().Contains(Key(bookingId, kind)); }

    public void Set(Guid bookingId, ClinicPrintKind kind, bool on)
    {
        lock (_gate)
        {
            var list = Load();
            var key = Key(bookingId, kind);
            list.Remove(key);
            if (on) list.Add(key);
            if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
            try
            {
                if (_path is null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(list));
            }
            catch { /* the choice still holds for this run */ }
        }
    }
}
