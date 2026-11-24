//JJQR - AI (21 Nov 2026) - Api implementation of IAdminSettingsService (web /admin/settings: General, Fees & payments, Opening hours, Privacy; lib/data/admin.ts):
//   GET/PUT /api/settings (full row: GET + patch; includes the flat fee schedule fee_consultation / fee_follow_up / fee_senior_pwd / fee_med_cert / discount_pct)
//   GET/PUT /api/admin/operating-hours (whole week) · GET/PUT /api/admin/payment-methods (PUT = the full list of accepted method names)
//   GET /api/services?activeOnly · POST /api/services · PUT /api/services/{id} · GET /api/medicines?q
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminSettingsService(IApiClient api) : IAdminSettingsService
{
    public async Task<ClinicSetting?> GetAsync(CancellationToken ct = default)
    {
        try { return await api.GetAsync<ClinicSetting>("/api/settings", null, ct); }
        catch (ApiNotFoundException) { return null; }
    }

    public async Task<AdminResult<ClinicSetting>> UpdateAsync(ClinicSettingsEdit edit, CancellationToken ct = default)
    {
        try
        {
            var s = await api.GetAsync<ClinicSetting>("/api/settings", null, ct);
            if (s is null) return AdminResult<ClinicSetting>.Fail("Clinic settings have not been set up on the server.");
            if (edit.ConsentVersion is { } next && next > s.ConsentVersion + 1)
                return AdminResult<ClinicSetting>.Fail("The consent version can only go up by one.");
            s.ClinicName = edit.ClinicName.Trim();
            s.Address = edit.Address.Trim();
            s.ContactNumber = ApiAdmin.Blank(edit.ContactNumber);
            s.Email = ApiAdmin.Blank(edit.Email);
            s.Description = ApiAdmin.Blank(edit.Description);
            s.DefaultPaymentMode = edit.DefaultPaymentMode;
            s.RefundPolicy = ApiAdmin.Blank(edit.RefundPolicy);
            s.PrivacyPolicyText = ApiAdmin.Blank(edit.PrivacyPolicyText);
            s.PrimaryColor = ApiAdmin.Blank(edit.PrimaryColor);
            s.SecondaryColor = ApiAdmin.Blank(edit.SecondaryColor);
            s.LogoUrl = ApiAdmin.Blank(edit.LogoUrl);
            s.FaviconUrl = ApiAdmin.Blank(edit.FaviconUrl);
            s.WebsiteUrl = ApiAdmin.Blank(edit.WebsiteUrl);
            s.FeeConsultation = edit.FeeConsultation;
            s.FeeFollowUp = edit.FeeFollowUp;
            s.FeeSeniorPwd = edit.FeeSeniorPwd;
            s.FeeMedCert = edit.FeeMedCert;
            s.DiscountPct = edit.DiscountPct;
            if (edit.ConsentVersion is { } published && published > s.ConsentVersion) s.ConsentVersion = published;
            await api.SendAsync(HttpMethod.Put, "/api/settings", s, null, ct);
            return AdminResult<ClinicSetting>.Ok(await api.GetAsync<ClinicSetting>("/api/settings", null, ct) ?? s);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<ClinicSetting>.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<OperatingHourEdit>> GetOperatingHoursAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<ClinicOperatingHour>>("/api/admin/operating-hours", null, ct) ?? [])
            .OrderBy(h => h.DayOfWeek).Select(h => new OperatingHourEdit(h.DayOfWeek, h.IsClosed, h.OpenTime, h.CloseTime)).ToList();

    public async Task<AdminResult<IReadOnlyList<OperatingHourEdit>>> SetOperatingHoursAsync(IReadOnlyList<OperatingHourEdit> hours, CancellationToken ct = default)
    {
        try
        {
            var body = hours.Select(h => new
            {
                day_of_week = h.DayOfWeek,
                is_closed = h.IsClosed,
                open_time = h.IsClosed ? null : h.OpenTime,
                close_time = h.IsClosed ? null : h.CloseTime,
            }).ToList();
            var saved = await api.SendAsync<List<ClinicOperatingHour>>(HttpMethod.Put, "/api/admin/operating-hours", body, null, ct) ?? [];
            IReadOnlyList<OperatingHourEdit> list = saved.OrderBy(h => h.DayOfWeek).Select(h => new OperatingHourEdit(h.DayOfWeek, h.IsClosed, h.OpenTime, h.CloseTime)).ToList();
            return AdminResult<IReadOnlyList<OperatingHourEdit>>.Ok(list);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<IReadOnlyList<OperatingHourEdit>>.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<ClinicAcceptedPaymentMethod>>("/api/admin/payment-methods", null, ct) ?? [])
            .Select(m => m.PaymentMethod).Distinct().ToList();

    public async Task<AdminResult<IReadOnlyList<PaymentMethod>>> SetPaymentMethodsAsync(IReadOnlyList<PaymentMethod> methods, CancellationToken ct = default)
    {
        try
        {
            var saved = await api.SendAsync<List<ClinicAcceptedPaymentMethod>>(HttpMethod.Put, "/api/admin/payment-methods",
                methods.Distinct().Select(m => m.ToString()).ToList(), null, ct) ?? [];
            IReadOnlyList<PaymentMethod> list = saved.Select(m => m.PaymentMethod).Distinct().ToList();
            return AdminResult<IReadOnlyList<PaymentMethod>>.Ok(list);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<IReadOnlyList<PaymentMethod>>.Fail(ex.Message); }
    }

    private static ServiceListItem Map(Service s) => new(s.ServiceId, s.Name, s.Category, s.Description, s.Price, s.IsActive);

    public async Task<IReadOnlyList<ServiceListItem>> ListServicesAsync(bool activeOnly = false, CancellationToken ct = default) =>
        (await api.GetAsync<List<Service>>("/api/services", ApiAdmin.Q(("activeOnly", activeOnly)), ct) ?? [])
            .OrderBy(s => s.Category).ThenBy(s => s.Name).Select(Map).ToList();

    public async Task<AdminResult<ServiceListItem>> CreateServiceAsync(string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default)
    {
        try
        {
            var row = await api.SendAsync<Service>(HttpMethod.Post, "/api/services", new Service
            {
                ServiceId = Guid.NewGuid(), Name = name.Trim(), Category = category, Price = price, Description = ApiAdmin.Blank(description), IsActive = isActive,
            }, null, ct);
            return row is null ? AdminResult<ServiceListItem>.Fail("The service could not be read back.") : AdminResult<ServiceListItem>.Ok(Map(row));
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<ServiceListItem>.Fail(ex.Message); }
    }

    public async Task<AdminResult<ServiceListItem>> UpdateServiceAsync(Guid id, string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default)
    {
        try
        {
            var row = await api.GetAsync<Service>($"/api/services/{id:D}", null, ct);
            if (row is null) return AdminResult<ServiceListItem>.Fail("Service not found.");
            row.Name = name.Trim(); row.Category = category; row.Price = price; row.Description = ApiAdmin.Blank(description); row.IsActive = isActive;
            await api.SendAsync(HttpMethod.Put, $"/api/services/{id:D}", row, null, ct);
            return AdminResult<ServiceListItem>.Ok(Map(row));
        }
        catch (ApiNotFoundException) { return AdminResult<ServiceListItem>.Fail("Service not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<ServiceListItem>.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<MedicineListItem>> SearchMedicinesAsync(string? q, CancellationToken ct = default) =>
        (await api.GetAsync<List<Medicine>>("/api/medicines", ApiAdmin.Q(("q", q)), ct) ?? [])
            .OrderBy(m => m.GenericName).Take(200).Select(m => new MedicineListItem(m.MedicineId, m.GenericName)).ToList();

    //JJQR - AI (Nov 24, 2026) - Admin > Settings > Payments switch, its own endpoint (the full settings PUT never changes it)
    sealed record AutoPaidAnswer(bool AutoPaidOnComplete);

    public async Task<AdminResult<bool>> SetAutoPaidOnCompleteAsync(bool enabled, CancellationToken ct = default)
    {
        try
        {
            var r = await api.SendAsync<AutoPaidAnswer>(HttpMethod.Put, "/api/settings/auto-paid-on-complete", new { Enabled = enabled }, null, ct);
            return AdminResult<bool>.Ok(r?.AutoPaidOnComplete ?? enabled);
        }
        catch (ApiNotFoundException) { return AdminResult<bool>.Fail("The clinic server does not have this switch yet (it needs the newest server update)."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<bool>.Fail(ex.Message); }
    }
    //JJQR - END
}
//JJQR - END
