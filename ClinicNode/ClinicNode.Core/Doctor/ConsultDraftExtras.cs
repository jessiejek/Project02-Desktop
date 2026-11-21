//JJQR - AI (21 Nov 2026) - web fields the shared Core/Clinical draft model does not carry yet (that file is outside the doctor lane):
// vaccination route / site / lot / expiry / manufacturer, and whether the doctor TYPED the Charge amount (web pfAmountEdited).
// Attached to the draft objects (no copy), read by ConsultationPage, ApiConsultationService and DoctorConsultationLocalService.
using System.Runtime.CompilerServices;
using ClinicNode.Core.Clinical;

namespace ClinicNode.Core.Doctor;

public sealed class VaccinationDetails
{
    public string? Manufacturer { get; set; }
    public string? Route { get; set; }
    public string? Site { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class DraftFeeState
{
    /// <summary>web pfAmountEdited: true only when the doctor typed the amount; otherwise the amount follows the computed total.</summary>
    public bool PfAmountEdited { get; set; }
    /// <summary>A certificate row exists on the server for this consultation (so Remove certificate must DELETE it).</summary>
    public bool CertificateOnServer { get; set; }
}

public static class ConsultDraftExtras
{
    private static readonly ConditionalWeakTable<VaccinationLine, VaccinationDetails> Vax = new();
    private static readonly ConditionalWeakTable<ConsultationDraft, DraftFeeState> Fee = new();

    public static VaccinationDetails Details(this VaccinationLine line) => Vax.GetValue(line, _ => new VaccinationDetails());
    public static DraftFeeState FeeState(this ConsultationDraft draft) => Fee.GetValue(draft, _ => new DraftFeeState());

    /// <summary>Carry the side data across when a service hands back a different draft instance.</summary>
    public static void CopyExtras(ConsultationDraft from, ConsultationDraft to)
    {
        if (ReferenceEquals(from, to)) return;
        to.FeeState().PfAmountEdited = from.FeeState().PfAmountEdited;
        to.FeeState().CertificateOnServer = from.FeeState().CertificateOnServer;
        for (var i = 0; i < Math.Min(from.Vaccinations.Count, to.Vaccinations.Count); i++)
        {
            var a = from.Vaccinations[i].Details(); var b = to.Vaccinations[i].Details();
            b.Manufacturer = a.Manufacturer; b.Route = a.Route; b.Site = a.Site; b.LotNumber = a.LotNumber; b.ExpiryDate = a.ExpiryDate;
        }
    }
}
//JJQR - END
