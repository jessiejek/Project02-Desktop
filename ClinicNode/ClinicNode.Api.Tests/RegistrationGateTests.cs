//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.4: a computer that cannot sync refuses to save locally
using System.Net;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Api.Tests;

public class RegistrationGateTests
{
    private static Task<ApiRig> StartAsync() => ApiRig.StartAsync(s =>
    {
        s.AddOnlineFirst<IStaffWalkInService, ApiStaffWalkInService, StaffWalkInService>();
        s.AddOnlineFirst<IStaffPatientsService, ApiStaffPatientsService, StaffPatientsService>();
    });

    private static async Task ReserveBlockAsync(ApiRig rig)
    {
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        foreach (var lane in new[] { "R", "S" })
            await QueueBlocks.SaveAsync(db, new QueueBlockReservation(ClinicApp.Domain.ClinicClock.Today, lane, 1, 20, 20, null, null));
    }

    [Fact]
    public async Task Unregistered_computer_with_the_server_down_refuses_the_save_and_saves_nothing()
    {
        await using var rig = await StartAsync();
        var patient = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Gate", "Test", new DateOnly(1990, 1, 1), SexType.Male));
        await ReserveBlockAsync(rig);
        rig.Sync.BlockedReason = "Register this computer for sync first.";
        rig.Http.Route = _ => throw new HttpRequestException("down");
        string? shown = null;
        void OnBlocked(string r) => shown = r;
        OnlineRouteStatus.LocalWriteBlocked += OnBlocked;
        try
        {
            var ex = await Assert.ThrowsAsync<LocalWriteBlockedException>(() =>
                rig.Get<IStaffWalkInService>().CheckInWalkInAsync(new WalkInRequest(patient.PatientId, rig.DoctorId, BookingId: Guid.NewGuid())));
            Assert.Contains("Register this computer", ex.Message);
            Assert.Equal(ex.Message, shown);                                    // the shell is told, so it can say why
        }
        finally { OnlineRouteStatus.LocalWriteBlocked -= OnBlocked; }
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, await db.Bookings.CountAsync(b => b.PatientId == patient.PatientId));   // nothing was saved
    }

    [Fact]
    public async Task Unregistered_computer_also_refuses_when_the_session_is_offline_only()
    {
        await using var rig = await ApiRig.StartAsync(s => s.AddOnlineFirst<IStaffPatientsService, ApiStaffPatientsService, StaffPatientsService>(), signedIn: false);
        rig.Sync.BlockedReason = "Register this computer for sync first.";
        var form = new QuickRegisterForm("No", null, "Save", new DateOnly(1990, 1, 1), SexType.Male, null, null, Guid.NewGuid());
        await Assert.ThrowsAsync<LocalWriteBlockedException>(() => rig.Get<IStaffPatientsService>().QuickRegisterAsync(form));
    }

    [Fact]
    public async Task Registered_computer_still_saves_locally_when_the_server_is_down()
    {
        await using var rig = await StartAsync();
        var patient = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Gate", "Ok", new DateOnly(1990, 1, 1), SexType.Male));
        await ReserveBlockAsync(rig);
        rig.Sync.BlockedReason = null;                                          // registered
        rig.Http.Route = _ => throw new HttpRequestException("down");
        var r = await rig.Get<IStaffWalkInService>().CheckInWalkInAsync(new WalkInRequest(patient.PatientId, rig.DoctorId, BookingId: Guid.NewGuid()));
        Assert.True(r.Succeeded, r.Error);
    }

    [Fact]
    public async Task Reads_are_never_blocked()
    {
        await using var rig = await StartAsync();
        rig.Sync.BlockedReason = "Register this computer for sync first.";
        rig.Http.Route = _ => throw new HttpRequestException("down");
        var rows = await rig.Get<IStaffPatientsService>().SearchAsync("");   // a read: still answered from the local copy
        Assert.NotNull(rows);
    }
}
//JJQR - END
