//JJQR - AI (21 Nov 2026) - HANDOFF to allen1 (lane change 5:39 PM): templates workspace contracts + local service, moved verbatim out of allen3's
// Core/DoctorExtra/DoctorExtraEditing.cs (TplRxLine / TplRxSet / TplFavorite / TplMedicine / LocalTpl / DoctorExtraOnline / DoctorResult stay there). Not copied to the Mac.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed record TplSoap(Guid Id, string Title, bool IsSystem, string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan);
/// <summary>Field is the DB enum name: ChiefComplaint / Subjective / Objective / Assessment / Plan.</summary>
public sealed record TplPhrase(Guid Id, string Field, string Label, string Body, int SortOrder);
public sealed record TplCert(Guid Id, string Title, bool IsSystem, string? DiagnosisText, string? Recommendations, string? PurposeException);
public sealed record TemplatesWorkspace(IReadOnlyList<TplSoap> Soap, IReadOnlyList<TplPhrase> Phrases, IReadOnlyList<TplRxSet> Rx,
    IReadOnlyList<TplFavorite> Favorites, IReadOnlyList<TplCert> Certificates);
public sealed record DoctorRestoreResult(bool Succeeded, string? Error, int Restored)
{
    public static DoctorRestoreResult Ok(int restored) => new(true, null, restored);
    public static DoctorRestoreResult Fail(string error) => new(false, error, 0);
}

public interface IDoctorTemplatesWorkspaceService
{
    Task<TemplatesWorkspace> GetWorkspaceAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<TplMedicine>> ListMedicinesAsync(CancellationToken ct = default);
    /// <summary>id null = create.</summary>
    Task<DoctorResult> SaveSoapAsync(Guid doctorId, Guid? id, TplSoap draft, CancellationToken ct = default);
    Task<DoctorResult> DeleteSoapAsync(Guid id, CancellationToken ct = default);
    Task<DoctorResult> SavePhraseAsync(Guid doctorId, Guid? id, string field, string label, string body, CancellationToken ct = default);
    Task<DoctorResult> DeletePhraseAsync(Guid id, CancellationToken ct = default);
    Task<DoctorResult> ReorderPhrasesAsync(Guid doctorId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<DoctorResult> SaveRxSetAsync(Guid doctorId, Guid? id, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default);
    Task<DoctorResult> DeleteRxSetAsync(Guid id, CancellationToken ct = default);
    Task<DoctorResult> SaveFavoriteAsync(Guid doctorId, Guid? id, TplFavorite draft, CancellationToken ct = default);
    Task<DoctorResult> DeleteFavoriteAsync(Guid id, CancellationToken ct = default);
    Task<DoctorResult> ReorderFavoritesAsync(Guid doctorId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<DoctorResult> SaveCertAsync(Guid doctorId, Guid? id, TplCert draft, CancellationToken ct = default);
    Task<DoctorResult> DeleteCertAsync(Guid id, CancellationToken ct = default);
    /// <summary>kind: soap | rx | cert | phrase (web RestoreBuiltInKind).</summary>
    Task<DoctorRestoreResult> RestoreBuiltInsAsync(Guid doctorId, string kind, CancellationToken ct = default);
}

public sealed class DoctorTemplatesWorkspaceService(IDbContextFactory<ClinicAppDbContext> factory) : IDoctorTemplatesWorkspaceService
{
    private static Task<DoctorResult> Offline() => Task.FromResult(DoctorResult.Fail(DoctorExtraOnline.NeedsServer));

    public async Task<TemplatesWorkspace> GetWorkspaceAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var soap = await db.SoapTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId).ToListAsync(ct);
        var phrases = await db.SoapPhrases.AsNoTracking().Where(p => p.DoctorId == doctorId).ToListAsync(ct);
        var certs = await db.MedicalCertificateTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId).ToListAsync(ct);
        return new TemplatesWorkspace(
            soap.OrderBy(t => t.Title, StringComparer.CurrentCulture).Select(t => new TplSoap(t.Id, t.Title, t.IsSystemTemplate, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan)).ToList(),
            phrases.Select(p => new TplPhrase(p.Id, p.Field.ToString(), p.Label, p.Body, p.SortOrder)).ToList(),
            await LocalTpl.RxSetsAsync(factory, doctorId, ct),
            await LocalTpl.FavoritesAsync(factory, doctorId, ct),
            certs.OrderBy(t => t.Title, StringComparer.CurrentCulture).Select(t => new TplCert(t.Id, t.Title, t.IsSystemTemplate, t.DiagnosisText, t.Recommendations, t.PurposeException)).ToList());
    }

    public Task<IReadOnlyList<TplMedicine>> ListMedicinesAsync(CancellationToken ct = default) => LocalTpl.MedicinesAsync(factory, ct);

    public Task<DoctorResult> SaveSoapAsync(Guid doctorId, Guid? id, TplSoap draft, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeleteSoapAsync(Guid id, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> SavePhraseAsync(Guid doctorId, Guid? id, string field, string label, string body, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeletePhraseAsync(Guid id, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> ReorderPhrasesAsync(Guid doctorId, IReadOnlyList<Guid> ids, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> SaveRxSetAsync(Guid doctorId, Guid? id, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeleteRxSetAsync(Guid id, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> SaveFavoriteAsync(Guid doctorId, Guid? id, TplFavorite draft, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeleteFavoriteAsync(Guid id, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> ReorderFavoritesAsync(Guid doctorId, IReadOnlyList<Guid> ids, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> SaveCertAsync(Guid doctorId, Guid? id, TplCert draft, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeleteCertAsync(Guid id, CancellationToken ct = default) => Offline();
    public Task<DoctorRestoreResult> RestoreBuiltInsAsync(Guid doctorId, string kind, CancellationToken ct = default) =>
        Task.FromResult(DoctorRestoreResult.Fail(DoctorExtraOnline.NeedsServer));
}

//JJQR - END
