// JJQR - AI (Nov 21, 2026) - after DevDataSeeder, make sure the demo doctor has weekly hours and a non-empty Today queue.
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Data;

public static class DemoClinicDay
{
    public static readonly Guid DemoDoctorId = Guid.Parse("60310394-4c74-4370-9cd3-47b22c4cbfb8");
    static readonly string[] DemoPatientCodes = ["MF-PEDS02", "MF-OB024", "MF-ELD70"];
    static readonly TimeOnly[] DemoSlots = [new(9, 0), new(10, 0), new(11, 0)];

    public static async Task EnsureAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        var doctor = await db.Doctors.Include(d => d.StaffAccount)
            .FirstOrDefaultAsync(d => d.DoctorId == DemoDoctorId
                || (d.StaffAccount != null && d.StaffAccount.Email == "doctor@clinic.test"), ct);
        if (doctor is null) return;

        var now = DateTimeOffset.UtcNow;
        // DayOfWeek 0 = Sunday; seed a daily 08:00–17:00 schedule including Saturday and Sunday.
        var haveDays = (await db.DoctorSchedules.Where(s => s.DoctorId == doctor.DoctorId).Select(s => s.DayOfWeek).ToListAsync(ct)).ToHashSet();
        for (short day = 0; day <= 6; day++)
        {
            if (haveDays.Contains(day)) continue;
            db.DoctorSchedules.Add(new DoctorSchedule
            {
                Id = Guid.NewGuid(),
                DoctorId = doctor.DoctorId,
                DayOfWeek = day,
                IsActive = true,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        var today = ClinicClock.Today;
        var hasToday = await db.Bookings.AnyAsync(b =>
            b.DoctorId == doctor.DoctorId
            && b.AppointmentDate == today
            && b.Status != BookingStatus.Cancelled
            && b.Status != BookingStatus.Expired, ct);
        // Never retarget the well-known dev booking IDs (those would collide across nodes on sync).
        // If Today is empty, insert new CheckedIn rows with a fresh GUID per node.
        if (!hasToday)
        {
            var patients = await db.Patients.AsNoTracking()
                .Where(p => DemoPatientCodes.Contains(p.PatientCode))
                .ToListAsync(ct);
            patients = DemoPatientCodes
                .Select(code => patients.FirstOrDefault(p => p.PatientCode == code))
                .Where(p => p is not null)
                .Cast<Patient>()
                .ToList();
            for (var i = 0; i < patients.Count; i++)
            {
                var start = DemoSlots[Math.Min(i, DemoSlots.Length - 1)];
                db.Bookings.Add(new Booking
                {
                    BookingId = Guid.NewGuid(),
                    PatientId = patients[i].PatientId,
                    DoctorId = doctor.DoctorId,
                    AppointmentDate = today,
                    SlotStartTime = start,
                    SlotEndTime = start.AddMinutes(30),
                    Status = BookingStatus.CheckedIn,
                    PaymentMode = PaymentMode.PayAtClinic,
                    VisitType = VisitType.FollowUp,
                    ConsultationFeeSnapshot = 500,
                    TotalFee = 500,
                    AmountDue = 500,
                    IsWalkIn = true,
                    QueueNumber = $"P-{i + 1:00}",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
// JJQR - END
