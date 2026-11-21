//JJQR - AI (Nov 21, 2026) - D-TEMPL "Consultation templates": SOAP, Diagnoses, Rx, Favorites, Phrases, Certificates.
// READ-ONLY on the node: templates are cloud-owned (SYNC-API-CONTRACT §6 / MAUI-ARCHITECTURE "Cloud wins; pulled down read-only"),
// so create / edit / delete / reorder / restore built-ins stay on the web portal.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Practice;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed record FavoriteMedicineItem(Guid Id, string GenericName, string Dosage, string Quantity, string? Instruction);
public sealed record CertificateTemplateItem(Guid Id, string Title, bool IsSystem, string? DiagnosisText, string? Recommendations, string? PurposeException);

public sealed record DoctorTemplates(
    IReadOnlyList<SoapTemplateItem> Soap,
    IReadOnlyList<DiagnosisTemplateItem> Diagnoses,
    IReadOnlyList<RxTemplateItem> Rx,
    IReadOnlyList<FavoriteMedicineItem> Favorites,
    IReadOnlyList<SoapPhraseItem> Phrases,
    IReadOnlyList<CertificateTemplateItem> Certificates);

public static class TemplateTabs
{
    public static readonly (string Id, string Label)[] All =
        [("soap", "SOAP"), ("diagnoses", "Diagnoses"), ("rx", "Rx"), ("favorites", "Favorites"), ("phrases", "Phrases"), ("certs", "Certificates")];

    public const bool ReadOnlyOnNode = true;
    public const string ReadOnlyNotice = "Templates are managed on the clinic web portal and synced to this computer. They are read-only here.";
}

public interface IDoctorTemplatesService
{
    Task<DoctorTemplates> GetAsync(Guid doctorId, CancellationToken ct = default);
}

public sealed class DoctorTemplatesService : IDoctorTemplatesService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IDoctorCatalogService _catalog;

    public DoctorTemplatesService(IDbContextFactory<ClinicAppDbContext> factory, IDoctorCatalogService catalog)
    {
        _factory = factory;
        _catalog = catalog;
    }

    public async Task<DoctorTemplates> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        var soap = await _catalog.ListSoapTemplatesAsync(doctorId, ct);
        var dx = await _catalog.ListDiagnosisTemplatesAsync(doctorId, ct);
        var rx = await _catalog.ListRxTemplatesAsync(doctorId, ct);
        var phrases = await _catalog.ListSoapPhrasesAsync(doctorId, ct);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var favs = await db.DoctorFavoriteMedicines.AsNoTracking().Where(f => f.DoctorId == doctorId).ToListAsync(ct);
        var certs = await db.MedicalCertificateTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId || t.IsSystemTemplate).ToListAsync(ct);
        return new DoctorTemplates(soap, dx, rx,
            favs.OrderBy(f => f.SortOrder).ThenBy(f => f.GenericName).Select(f => new FavoriteMedicineItem(f.Id, f.GenericName, f.Dosage, f.Quantity, f.Instruction)).ToList(),
            phrases,
            certs.OrderBy(t => t.Title).Select(t => new CertificateTemplateItem(t.Id, t.Title, t.IsSystemTemplate, t.DiagnosisText, t.Recommendations, t.PurposeException)).ToList());
    }

    /// <summary>Label for a SOAP phrase field.</summary>
    public static string FieldLabel(SoapField f) => f.ToString();
}
//JJQR - END
