//JJQR - AI (21 Nov 2026) - Jessie: clinic fee and setting changes stay ONLINE-ONLY.
// The offline side of IAdminSettingsService for the online-first router (AdminModule): reads come from the local copy, every write is refused
// with a "needs an internet connection" answer. Nothing is written to the local DB and no clinic_settings / operating-hours / payment-method /
// service outbox row is queued, so a fee change can never be saved "to sync later". Online writes go to the Api twin (PUT /api/settings,
// PUT /api/admin/operating-hours, PUT /api/admin/payment-methods, /api/services).
// The plain local AdminSettingsService is left unchanged (its own tests still use it directly); it is just no longer the router's offline side.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Admin;

public sealed class OnlineOnlyAdminSettingsService : IAdminSettingsService
{
    public const string NeedsInternet = "Clinic settings and fees can only be changed with an internet connection to the clinic server. Nothing was saved; connect and try again.";

    readonly AdminSettingsService _reads;

    public OnlineOnlyAdminSettingsService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth) =>
        _reads = new AdminSettingsService(factory, outbox, auth, sync: null); // used for reads only

    // ── reads: this computer's copy ──
    public Task<ClinicSetting?> GetAsync(CancellationToken ct = default) => _reads.GetAsync(ct);
    public Task<IReadOnlyList<OperatingHourEdit>> GetOperatingHoursAsync(CancellationToken ct = default) => _reads.GetOperatingHoursAsync(ct);
    public Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(CancellationToken ct = default) => _reads.GetPaymentMethodsAsync(ct);
    public Task<IReadOnlyList<ServiceListItem>> ListServicesAsync(bool activeOnly = false, CancellationToken ct = default) => _reads.ListServicesAsync(activeOnly, ct);
    public Task<IReadOnlyList<MedicineListItem>> SearchMedicinesAsync(string? q, CancellationToken ct = default) => _reads.SearchMedicinesAsync(q, ct);

    // ── writes: refused offline (no DB write, no outbox row) ──
    public Task<AdminResult<ClinicSetting>> UpdateAsync(ClinicSettingsEdit edit, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<ClinicSetting>.Fail(NeedsInternet));

    public Task<AdminResult<IReadOnlyList<OperatingHourEdit>>> SetOperatingHoursAsync(IReadOnlyList<OperatingHourEdit> hours, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<IReadOnlyList<OperatingHourEdit>>.Fail(NeedsInternet));

    public Task<AdminResult<IReadOnlyList<PaymentMethod>>> SetPaymentMethodsAsync(IReadOnlyList<PaymentMethod> methods, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<IReadOnlyList<PaymentMethod>>.Fail(NeedsInternet));

    public Task<AdminResult<ServiceListItem>> CreateServiceAsync(string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<ServiceListItem>.Fail(NeedsInternet));

    public Task<AdminResult<ServiceListItem>> UpdateServiceAsync(Guid id, string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<ServiceListItem>.Fail(NeedsInternet));

    //JJQR - AI (Nov 24, 2026) - "Mark the visit paid when the doctor completes it": online only
    public Task<AdminResult<bool>> SetAutoPaidOnCompleteAsync(bool enabled, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<bool>.Fail(NeedsInternet));
    //JJQR - END
}
//JJQR - END
