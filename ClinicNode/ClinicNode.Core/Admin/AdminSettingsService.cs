// JJQR - AI (Nov 21, 2026) - ported from SettingsController + AdminController + ServicesController + LookupsController medicines.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Sync;
// JJQR - END

namespace ClinicNode.Core.Admin;

public sealed record ClinicSettingsEdit(
    string ClinicName,
    string Address,
    string? ContactNumber,
    string? Email,
    string? Description,
    PaymentMode DefaultPaymentMode,
    string? RefundPolicy,
    string? PrivacyPolicyText,
    string? PrimaryColor,
    string? SecondaryColor,
    string? LogoUrl,
    string? FaviconUrl,
    string? WebsiteUrl,
    decimal FeeConsultation,
    decimal FeeFollowUp,
    decimal FeeSeniorPwd,
    decimal FeeMedCert,
    decimal DiscountPct,
    int? ConsentVersion = null);

public sealed record OperatingHourEdit(short DayOfWeek, bool IsClosed, TimeOnly? OpenTime, TimeOnly? CloseTime);

public sealed record ServiceListItem(Guid ServiceId, string Name, ServiceCategory Category, string? Description, decimal Price, bool IsActive);

public sealed record MedicineListItem(Guid MedicineId, string GenericName);

public interface IAdminSettingsService
{
    Task<ClinicSetting?> GetAsync(CancellationToken ct = default);
    Task<AdminResult<ClinicSetting>> UpdateAsync(ClinicSettingsEdit edit, CancellationToken ct = default);
    Task<IReadOnlyList<OperatingHourEdit>> GetOperatingHoursAsync(CancellationToken ct = default);
    Task<AdminResult<IReadOnlyList<OperatingHourEdit>>> SetOperatingHoursAsync(IReadOnlyList<OperatingHourEdit> hours, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(CancellationToken ct = default);
    Task<AdminResult<IReadOnlyList<PaymentMethod>>> SetPaymentMethodsAsync(IReadOnlyList<PaymentMethod> methods, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceListItem>> ListServicesAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<AdminResult<ServiceListItem>> CreateServiceAsync(string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default);
    Task<AdminResult<ServiceListItem>> UpdateServiceAsync(Guid id, string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default);
    Task<IReadOnlyList<MedicineListItem>> SearchMedicinesAsync(string? q, CancellationToken ct = default);
    //JJQR - AI (Nov 24, 2026) - owner: Admin > Settings > Payments switch "Mark the visit paid when the doctor completes it". Online only (PUT api/settings/auto-paid-on-complete);
    // the server does the marking when the visit is completed. Returns the saved value.
    Task<AdminResult<bool>> SetAutoPaidOnCompleteAsync(bool enabled, CancellationToken ct = default);
    //JJQR - END
}

public sealed class AdminSettingsService : IAdminSettingsService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;

    // JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge? _sync;
    // JJQR - END

    public AdminSettingsService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, ISyncBridge? sync = null)
    {
        // JJQR - AI (Nov 21, 2026)
        _sync = sync;
        // JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
    }

    public async Task<ClinicSetting?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct);
    }

    public async Task<AdminResult<ClinicSetting>> UpdateAsync(ClinicSettingsEdit edit, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<ClinicSetting>.Fail("Only an administrator can change clinic settings.");

        var check = new AdminCheck()
            .Required("Clinic name", edit.ClinicName, 120)
            .Required("Address", edit.Address, 300)
            .Phone("Contact number", edit.ContactNumber)
            .Email("Email", edit.Email)
            .Optional("Description", edit.Description, 2000)
            .Optional("Refund policy", edit.RefundPolicy, 4000)
            .Optional("Privacy policy", edit.PrivacyPolicyText, 20000)
            .Hex("Primary colour", edit.PrimaryColor)
            .Hex("Secondary colour", edit.SecondaryColor)
            .Url("Logo address", edit.LogoUrl)
            .Url("Favicon address", edit.FaviconUrl)
            .Url("Website address", edit.WebsiteUrl)
            .Money("Consultation fee", edit.FeeConsultation)
            .Money("Follow-up fee", edit.FeeFollowUp)
            .Money("Senior/PWD fee", edit.FeeSeniorPwd)
            .Money("Medical certificate fee", edit.FeeMedCert)
            .Range("Discount", edit.DiscountPct, 0m, 1m);
        if (!Enum.IsDefined(edit.DefaultPaymentMode))
            check.Fail("Choose a valid default payment mode.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) return AdminResult<ClinicSetting>.Fail("Clinic settings were not found.");
        if (edit.ConsentVersion is { } next && next > settings.ConsentVersion + 1)
            check.Fail("Consent version can only go up by one at a time.");
        if (check.HasErrors) return AdminResult<ClinicSetting>.Fail(check.Message!);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        settings.ClinicName = edit.ClinicName.Trim();
        settings.Address = edit.Address.Trim();
        settings.ContactNumber = string.IsNullOrWhiteSpace(edit.ContactNumber) ? null : edit.ContactNumber.Trim();
        settings.Email = string.IsNullOrWhiteSpace(edit.Email) ? null : edit.Email.Trim();
        settings.Description = string.IsNullOrWhiteSpace(edit.Description) ? null : edit.Description.Trim();
        settings.DefaultPaymentMode = edit.DefaultPaymentMode;
        settings.RefundPolicy = string.IsNullOrWhiteSpace(edit.RefundPolicy) ? null : edit.RefundPolicy.Trim();
        settings.PrimaryColor = string.IsNullOrWhiteSpace(edit.PrimaryColor) ? null : edit.PrimaryColor.Trim();
        settings.SecondaryColor = string.IsNullOrWhiteSpace(edit.SecondaryColor) ? null : edit.SecondaryColor.Trim();
        settings.LogoUrl = string.IsNullOrWhiteSpace(edit.LogoUrl) ? null : edit.LogoUrl.Trim();
        settings.FaviconUrl = string.IsNullOrWhiteSpace(edit.FaviconUrl) ? null : edit.FaviconUrl.Trim();
        settings.WebsiteUrl = string.IsNullOrWhiteSpace(edit.WebsiteUrl) ? null : edit.WebsiteUrl.Trim();
        settings.PrivacyPolicyText = string.IsNullOrWhiteSpace(edit.PrivacyPolicyText) ? null : edit.PrivacyPolicyText.Trim();
        settings.FeeConsultation = edit.FeeConsultation;
        settings.FeeFollowUp = edit.FeeFollowUp;
        settings.FeeSeniorPwd = edit.FeeSeniorPwd;
        settings.FeeMedCert = edit.FeeMedCert;
        settings.DiscountPct = edit.DiscountPct;
        if (edit.ConsentVersion is { } published && published > settings.ConsentVersion)
        {
            settings.ConsentVersion = published;
            AdminAudit.Add(db, AuditEntityType.Settings, Guid.Empty, "Consent version published", _auth.Current.UserId, $"Consent version → {settings.ConsentVersion}");
        }
        settings.UpdatedByUserId = _auth.Current.UserId;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        //JJQR - AI (21 Nov 2026) - Jessie: clinic fee and setting changes stay online-only. No clinic_settings outbox row is queued any more
        // (the server rejects it as cloud-owned; allen2 also drops such rows at push time). The local save below is unchanged.
        //JJQR - END
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<ClinicSetting>.Ok(settings);
    }

    public async Task<IReadOnlyList<OperatingHourEdit>> GetOperatingHoursAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.ClinicOperatingHours.AsNoTracking().OrderBy(h => h.DayOfWeek).ToListAsync(ct);
        return rows.Select(h => new OperatingHourEdit(h.DayOfWeek, h.IsClosed, h.OpenTime, h.CloseTime)).ToList();
    }

    public async Task<AdminResult<IReadOnlyList<OperatingHourEdit>>> SetOperatingHoursAsync(IReadOnlyList<OperatingHourEdit> hours, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<IReadOnlyList<OperatingHourEdit>>.Fail("Only an administrator can change operating hours.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.ClinicOperatingHours.ToListAsync(ct);
        foreach (var h in hours)
        {
            var row = existing.SingleOrDefault(x => x.DayOfWeek == h.DayOfWeek);
            if (row is null)
            {
                db.ClinicOperatingHours.Add(new ClinicOperatingHour
                {
                    DayOfWeek = h.DayOfWeek,
                    IsClosed = h.IsClosed,
                    OpenTime = h.OpenTime,
                    CloseTime = h.CloseTime,
                });
            }
            else
            {
                row.IsClosed = h.IsClosed;
                row.OpenTime = h.OpenTime;
                row.CloseTime = h.CloseTime;
            }
        }
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<IReadOnlyList<OperatingHourEdit>>.Ok(await GetOperatingHoursAsync(ct));
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ClinicAcceptedPaymentMethods.AsNoTracking().Select(m => m.PaymentMethod).ToListAsync(ct);
    }

    public async Task<AdminResult<IReadOnlyList<PaymentMethod>>> SetPaymentMethodsAsync(IReadOnlyList<PaymentMethod> methods, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<IReadOnlyList<PaymentMethod>>.Fail("Only an administrator can change payment methods.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ClinicAcceptedPaymentMethods.RemoveRange(db.ClinicAcceptedPaymentMethods);
        foreach (var m in methods.Distinct())
            db.ClinicAcceptedPaymentMethods.Add(new ClinicAcceptedPaymentMethod { PaymentMethod = m });
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<IReadOnlyList<PaymentMethod>>.Ok(await GetPaymentMethodsAsync(ct));
    }

    public async Task<IReadOnlyList<ServiceListItem>> ListServicesAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Services.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new ServiceListItem(s.ServiceId, s.Name, s.Category, s.Description, s.Price, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<AdminResult<ServiceListItem>> CreateServiceAsync(string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<ServiceListItem>.Fail("Only an administrator can add a service.");
        var check = new AdminCheck().Required("Name", name, 200).Money("Price", price).Optional("Description", description, 2000);
        if (check.HasErrors) return AdminResult<ServiceListItem>.Fail(check.Message!);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = new Service
        {
            ServiceId = Guid.NewGuid(),
            Name = name.Trim(),
            Category = category,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Price = price,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Services.Add(row);
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<ServiceListItem>.Ok(new ServiceListItem(row.ServiceId, row.Name, row.Category, row.Description, row.Price, row.IsActive));
    }

    public async Task<AdminResult<ServiceListItem>> UpdateServiceAsync(Guid id, string name, ServiceCategory category, decimal price, string? description, bool isActive, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<ServiceListItem>.Fail("Only an administrator can edit a service.");
        var check = new AdminCheck().Required("Name", name, 200).Money("Price", price).Optional("Description", description, 2000);
        if (check.HasErrors) return AdminResult<ServiceListItem>.Fail(check.Message!);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var service = await db.Services.SingleOrDefaultAsync(s => s.ServiceId == id, ct);
        if (service is null) return AdminResult<ServiceListItem>.Fail("Service not found.");
        service.Name = name.Trim();
        service.Category = category;
        service.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        service.Price = price;
        service.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<ServiceListItem>.Ok(new ServiceListItem(service.ServiceId, service.Name, service.Category, service.Description, service.Price, service.IsActive));
    }

    public async Task<IReadOnlyList<MedicineListItem>> SearchMedicinesAsync(string? q, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Medicines.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(m => m.GenericName.Contains(q.Trim()));
        return await query.OrderBy(m => m.GenericName)
            .Select(m => new MedicineListItem(m.MedicineId, m.GenericName))
            .Take(200)
            .ToListAsync(ct);
    }

    //JJQR - AI (Nov 24, 2026) - the switch lives on the server (it marks the payment there); this computer never changes it
    public Task<AdminResult<bool>> SetAutoPaidOnCompleteAsync(bool enabled, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<bool>.Fail(OnlineOnlyAdminSettingsService.NeedsInternet));
    //JJQR - END
}
// JJQR - END
