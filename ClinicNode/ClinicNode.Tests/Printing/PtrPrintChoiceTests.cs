//JJQR - AI (Nov 24, 2026) - "Include PTR in print": off by default, per visit and per document, remembered on this computer.
using ClinicNode.Core.Printing;
using Xunit;

namespace ClinicNode.Tests.Printing;

public class PtrPrintChoiceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "cn-ptr-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Off_by_default_per_visit_and_document_and_remembered_after_a_restart()
    {
        var visit = Guid.NewGuid(); var other = Guid.NewGuid();
        var c = new PtrPrintChoice(_dir);
        Assert.False(c.IsOn(visit, ClinicPrintKind.Prescription));               // default: off
        c.Set(visit, ClinicPrintKind.Prescription, true);
        Assert.True(c.IsOn(visit, ClinicPrintKind.Prescription));
        Assert.False(c.IsOn(visit, ClinicPrintKind.LabRequest));                 // another document of the same visit
        Assert.False(c.IsOn(other, ClinicPrintKind.Prescription));               // another visit
        Assert.True(new PtrPrintChoice(_dir).IsOn(visit, ClinicPrintKind.Prescription));   // after a restart
        c.Set(visit, ClinicPrintKind.Prescription, false);
        Assert.False(new PtrPrintChoice(_dir).IsOn(visit, ClinicPrintKind.Prescription));
    }

    [Fact]
    public void An_unreadable_file_or_no_folder_just_means_off()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ptr-print.json"), "{not json");
        Assert.False(new PtrPrintChoice(_dir).IsOn(Guid.NewGuid(), ClinicPrintKind.MedicalCertificate));
        var memoryOnly = new PtrPrintChoice(null);
        var v = Guid.NewGuid();
        memoryOnly.Set(v, ClinicPrintKind.LabRequest, true);
        Assert.True(memoryOnly.IsOn(v, ClinicPrintKind.LabRequest));            // holds for this run even without a folder
    }
}
//JJQR - END
