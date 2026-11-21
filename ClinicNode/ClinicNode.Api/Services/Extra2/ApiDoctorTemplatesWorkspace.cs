//JJQR - AI (21 Nov 2026) - HANDOFF to allen1 (lane change 5:39 PM): templates workspace API service, moved verbatim out of allen3's
// ApiDoctorExtraPrescriptions.cs. Uses ApiTplWire (SoapDto / PhraseDto / CertDto / RestoreDto...) which stays in that file. Not copied to the Mac.
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;
using static ClinicNode.Api.Services.ApiDoctorExtra;

namespace ClinicNode.Api.Services;

public sealed class ApiDoctorTemplatesWorkspaceService(IApiClient api) : IDoctorTemplatesWorkspaceService
{
    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<TemplatesWorkspace> GetWorkspaceAsync(Guid doctorId, CancellationToken ct = default)
    {
        var q = Q(("doctorId", Id(doctorId)));
        var soap = await api.GetAsync<List<ApiTplWire.SoapDto>>("/api/soap-templates", q, ct) ?? [];
        var phrases = await api.GetAsync<List<ApiTplWire.PhraseDto>>("/api/soap-phrases", q, ct) ?? [];
        var certs = await api.GetAsync<List<ApiTplWire.CertDto>>("/api/medical-certificate-templates", q, ct) ?? [];
        return new TemplatesWorkspace(
            soap.Select(t => new TplSoap(t.Id, t.Title ?? "", t.IsSystemTemplate, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan))
                .OrderBy(t => t.Title, StringComparer.CurrentCulture).ToList(),
            phrases.Select(p => new TplPhrase(p.Id, p.Field.ToString(), p.Label ?? "", p.Body ?? "", p.SortOrder)).ToList(),
            await ApiTplWire.RxSetsAsync(api, doctorId, ct),
            await ApiTplWire.FavoritesAsync(api, doctorId, ct),
            certs.Select(t => new TplCert(t.Id, t.Title ?? "", t.IsSystemTemplate, t.DiagnosisText, t.Recommendations, t.PurposeException))
                .OrderBy(t => t.Title, StringComparer.CurrentCulture).ToList());
    }

    public Task<IReadOnlyList<TplMedicine>> ListMedicinesAsync(CancellationToken ct = default) => ApiTplWire.MedicinesAsync(api, ct);

    public Task<DoctorResult> SaveSoapAsync(Guid doctorId, Guid? id, TplSoap draft, CancellationToken ct = default)
    {
        var body = new
        {
            title = draft.Title.Trim(),
            is_system_template = false,
            chief_complaint = Clean(draft.ChiefComplaint),
            subjective = Clean(draft.Subjective),
            objective = Clean(draft.Objective),
            assessment = Clean(draft.Assessment),
            plan = Clean(draft.Plan),
        };
        return Write(async () => id is { } existing
            ? (await api.SendAsync<ApiTplWire.SoapDto>(HttpMethod.Put, $"/api/soap-templates/{Id(existing)}", body, null, ct))?.Id ?? existing
            : (await api.SendAsync<ApiTplWire.SoapDto>(HttpMethod.Post, "/api/soap-templates", body, Q(("doctorId", Id(doctorId))), ct))?.Id,
            "SOAP template was not saved. Your edits are still here — try again.");
    }

    public Task<DoctorResult> DeleteSoapAsync(Guid id, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/soap-templates/{Id(id)}", null, null, ct), id, "Couldn’t delete the template.");

    public Task<DoctorResult> SavePhraseAsync(Guid doctorId, Guid? id, string field, string label, string body, CancellationToken ct = default)
    {
        var input = new { field, label = label.Trim(), body = body.Trim() };
        return Write(async () => id is { } existing
            ? (await api.SendAsync<ApiTplWire.PhraseDto>(HttpMethod.Put, $"/api/soap-phrases/{Id(existing)}", input, null, ct))?.Id ?? existing
            : (await api.SendAsync<ApiTplWire.PhraseDto>(HttpMethod.Post, "/api/soap-phrases", input, Q(("doctorId", Id(doctorId))), ct))?.Id,
            "Phrase was not saved. Your edits are still here — try again.");
    }

    public Task<DoctorResult> DeletePhraseAsync(Guid id, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/soap-phrases/{Id(id)}", null, null, ct), id, "Couldn’t delete the phrase.");

    public Task<DoctorResult> ReorderPhrasesAsync(Guid doctorId, IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Put, "/api/soap-phrases/order", new { ids }, Q(("doctorId", Id(doctorId))), ct), null,
            "Couldn’t save the new order. It was put back.");

    public Task<DoctorResult> SaveRxSetAsync(Guid doctorId, Guid? id, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default)
    {
        var body = ApiTplWire.RxTemplateBody(doctorId, title, lines);
        return Write(async () => id is { } existing
            ? (await api.SendAsync<ApiTplWire.RxTemplateDto>(HttpMethod.Put, $"/api/prescription-templates/{Id(existing)}", body, null, ct))?.TemplateId ?? existing
            : (await api.SendAsync<ApiTplWire.RxTemplateDto>(HttpMethod.Post, "/api/prescription-templates", body, null, ct))?.TemplateId,
            "Prescription set was not saved. Your edits are still here — try again.");
    }

    public Task<DoctorResult> DeleteRxSetAsync(Guid id, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/prescription-templates/{Id(id)}", null, null, ct), id, "Couldn’t delete the prescription set.");

    public Task<DoctorResult> SaveFavoriteAsync(Guid doctorId, Guid? id, TplFavorite draft, CancellationToken ct = default)
    {
        var body = ApiTplWire.FavoriteBody(draft.MedicineId, draft.GenericName, draft.Dosage, draft.Quantity, draft.Instruction);
        return Write(async () => id is { } existing
            ? (await api.SendAsync<ApiTplWire.FavoriteDto>(HttpMethod.Put, $"/api/doctor-favorite-medicines/{Id(existing)}", body, null, ct))?.Id ?? existing
            : (await api.SendAsync<ApiTplWire.FavoriteDto>(HttpMethod.Post, "/api/doctor-favorite-medicines", body, Q(("doctorId", Id(doctorId))), ct))?.Id,
            "Favorite was not saved. Your edits are still here — try again.");
    }

    public Task<DoctorResult> DeleteFavoriteAsync(Guid id, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/doctor-favorite-medicines/{Id(id)}", null, null, ct), id, "Couldn’t delete the favorite.");

    public Task<DoctorResult> ReorderFavoritesAsync(Guid doctorId, IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Put, "/api/doctor-favorite-medicines/order", new { ids }, Q(("doctorId", Id(doctorId))), ct), null,
            "Couldn’t save the new order. It was put back.");

    public Task<DoctorResult> SaveCertAsync(Guid doctorId, Guid? id, TplCert draft, CancellationToken ct = default)
    {
        var body = new
        {
            title = draft.Title.Trim(),
            is_system_template = false,
            diagnosis_text = Clean(draft.DiagnosisText),
            recommendations = Clean(draft.Recommendations),
            purpose_exception = Clean(draft.PurposeException),
        };
        return Write(async () => id is { } existing
            ? (await api.SendAsync<ApiTplWire.CertDto>(HttpMethod.Put, $"/api/medical-certificate-templates/{Id(existing)}", body, null, ct))?.Id ?? existing
            : (await api.SendAsync<ApiTplWire.CertDto>(HttpMethod.Post, "/api/medical-certificate-templates", body, Q(("doctorId", Id(doctorId))), ct))?.Id,
            "Certificate template was not saved. Your edits are still here — try again.");
    }

    public Task<DoctorResult> DeleteCertAsync(Guid id, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/medical-certificate-templates/{Id(id)}", null, null, ct), id, "Couldn’t delete the certificate template.");

    public async Task<DoctorRestoreResult> RestoreBuiltInsAsync(Guid doctorId, string kind, CancellationToken ct = default)
    {
        try
        {
            var r = await api.SendAsync<ApiTplWire.RestoreDto>(HttpMethod.Post, "/api/templates/restore-built-ins", null, Q(("doctorId", Id(doctorId)), ("kind", kind)), ct);
            return DoctorRestoreResult.Ok(r?.Restored ?? 0);
        }
        catch (Exception ex) when (IsServerAnswer(ex))
        {
            return DoctorRestoreResult.Fail("Couldn’t restore built-in templates. Please try again.");
        }
    }
}
//JJQR - END
