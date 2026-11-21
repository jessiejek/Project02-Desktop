// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Patients;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Admin;

public class AdminSettingsAuditReportTests
{
    [Fact]
    public async Task Settings_reject_negative_fees_and_consent_jumps()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminSettingsService>();
        var current = await svc.GetAsync();
        Assert.NotNull(current);

        var negative = await svc.UpdateAsync(From(current!, FeeConsultation: -1));
        Assert.False(negative.Succeeded);
        Assert.Contains("cannot be negative", negative.Error, StringComparison.OrdinalIgnoreCase);

        var jump = await svc.UpdateAsync(From(current!, ConsentVersion: current!.ConsentVersion + 2));
        Assert.False(jump.Succeeded);
        Assert.Contains("Consent version", jump.Error, StringComparison.OrdinalIgnoreCase);

        var ok = await svc.UpdateAsync(From(current!, FeeConsultation: 500m, ConsentVersion: current.ConsentVersion + 1));
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal(500m, ok.Value!.FeeConsultation);
        Assert.Equal(current.ConsentVersion + 1, ok.Value.ConsentVersion);
    }

    [Fact]
    public async Task Operating_hours_and_payment_methods_round_trip()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminSettingsService>();
        var hours = await svc.SetOperatingHoursAsync([
            new OperatingHourEdit(0, true, null, null),
            new OperatingHourEdit(1, false, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        ]);
        Assert.True(hours.Succeeded, hours.Error);
        Assert.Contains(hours.Value!, h => h.DayOfWeek == 0 && h.IsClosed);

        var methods = await svc.SetPaymentMethodsAsync([PaymentMethod.Cash, PaymentMethod.GCash]);
        Assert.True(methods.Succeeded, methods.Error);
        Assert.Equal(2, methods.Value!.Count);
        Assert.Contains(PaymentMethod.GCash, methods.Value);
    }

    [Fact]
    public async Task Audit_search_excludes_access_and_auth_by_default()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var adminPatients = host.Get<IAdminPatientService>();
        var audit = host.Get<IAdminAuditLogService>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Rita", "Go", new DateOnly(1975, 4, 4), SexType.Female));
        var detail = await adminPatients.GetAsync(patient.PatientId); // writes an Access row
        Assert.True(detail.Succeeded, detail.Error);

        var changeLog = await audit.SearchAsync(null, null);
        Assert.DoesNotContain(changeLog.Items, i => i.EntityType is AuditEntityType.Access or AuditEntityType.Auth);

        var accessOnly = await audit.SearchAsync(null, AuditEntityType.Access);
        Assert.Contains(accessOnly.Items, i => i.EntityId == patient.PatientId && i.EntityType == AuditEntityType.Access);

        var patientAccess = await adminPatients.GetAccessLogAsync(patient.PatientId);
        Assert.Contains(patientAccess, a => a.Area == "Patient record");
    }

    [Fact]
    public async Task Daily_summary_does_not_count_unpaid_on_cancelled_bookings()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var bookings = host.Get<IAdminBookingService>();
        var reports = host.Get<IAdminReportService>();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        await host.LoginAdminAsync();

        var patient = await patients.CreateAsync(new NewPatientRequest("Sam", "Lim", new DateOnly(1982, 8, 8), SexType.Male));
        var date = ClinicClock.Today.AddDays(4);
        for (var i = 0; i < 14; i++)
        {
            if (await bookings.ClosedReasonAsync(date, doctorId) is null) break;
            date = date.AddDays(1);
        }
        var created = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(13, 0), new TimeOnly(13, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.True(created.Succeeded, created.Error);
        var cancelled = await bookings.UpdateStatusAsync(created.Value!.BookingId, BookingStatus.Cancelled, "no longer needed");
        Assert.True(cancelled.Succeeded, cancelled.Error);

        var summary = await reports.DailyBookingSummaryAsync();
        var row = summary.Single(r => r.AppointmentDate == date);
        Assert.Equal(1, row.TotalBookings);
        Assert.Equal(0, row.UnpaidCount);
    }

    [Fact]
    public async Task Dashboard_loads_for_admin()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var dash = await host.Get<IAdminDashboardService>().LoadAsync();
        Assert.Equal(ClinicClock.Today, dash.Today);
        Assert.True(dash.AppointmentsToday >= 0);
        Assert.NotNull(dash.DoctorLoads);
        Assert.NotNull(dash.LatestBookings);
    }

    static ClinicSettingsEdit From(ClinicApp.Domain.Entities.ClinicSetting s, decimal? FeeConsultation = null, int? ConsentVersion = null) =>
        new(
            s.ClinicName, s.Address, s.ContactNumber, s.Email, s.Description, s.DefaultPaymentMode,
            s.RefundPolicy, s.PrivacyPolicyText, s.PrimaryColor, s.SecondaryColor, s.LogoUrl, s.FaviconUrl, s.WebsiteUrl,
            FeeConsultation ?? s.FeeConsultation, s.FeeFollowUp, s.FeeSeniorPwd, s.FeeMedCert, s.DiscountPct,
            ConsentVersion);
}
// JJQR - END
