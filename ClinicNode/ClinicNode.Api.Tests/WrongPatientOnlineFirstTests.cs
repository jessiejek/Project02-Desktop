//JJQR - AI (2026-11-26) - GAV-11: "Wrong patient?" on the desktop: online only (Offline mode / no connection = "needs an internet connection", no request),
// the POST body the server expects, its 409 questions (same-day visit, same name + birthday), and "sync first" (unsent work for the visit stops the move).
using System.Net;
using System.Text.Json;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Admin;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Api.Tests;

public class WrongPatientOnlineFirstTests
{
    static Task<ApiRig> Rig() => ApiRig.StartAsync(s =>
    {
        s.TryAddSingleton<OfflineVisitReassignService>();
        s.AddOnlineFirst<IVisitReassignService, ApiVisitReassignService, OfflineVisitReassignService>();
    });

    static readonly Guid Booking = Guid.Parse("11111111-2222-3333-4444-555555555555");
    static readonly Guid Person1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid Person2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    static readonly Guid Latex = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    static readonly Guid Penicillin = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    static object PreviewJson(object? target = null) => new Dictionary<string, object?>
    {
        ["booking_id"] = Booking, ["appointment_date"] = "2026-11-26", ["queue_number"] = "R-015", ["status"] = "Completed",
        ["consultation_id"] = Guid.NewGuid(), ["consultation_status"] = "Completed", ["payment_status"] = "Paid", ["or_number"] = "OR-0042",
        ["patient"] = new Dictionary<string, object?> { ["patient_id"] = Person1, ["patient_code"] = "MF-000001", ["first_name"] = "Fake", ["last_name"] = "Personone" },
        ["allergies"] = new[]
        {
            new Dictionary<string, object?> { ["id"] = Latex, ["allergen"] = "Latex", ["reaction"] = null, ["severity"] = null, ["is_nkda"] = false, ["added_during_visit"] = true },
            new Dictionary<string, object?> { ["id"] = Penicillin, ["allergen"] = "Penicillin", ["reaction"] = "Rash", ["severity"] = null, ["is_nkda"] = false, ["added_during_visit"] = false },
        },
        ["counts"] = new Dictionary<string, int> { ["consultations"] = 1, ["prescriptions"] = 1, ["lab_orders"] = 0, ["payments"] = 1 },
        ["target"] = target,
    };

    [Fact]
    public async Task Online_preview_is_read_and_the_move_posts_what_the_server_expects()
    {
        await using var rig = await Rig();
        string? posted = null;
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == $"/api/bookings/{Booking:D}/reassign-preview") return FakeHandler.Json(PreviewJson());
            if (path == $"/api/bookings/{Booking:D}/reassign-patient" && r.Method == HttpMethod.Post)
            {
                posted = r.Content!.ReadAsStringAsync().Result;
                return FakeHandler.Json(new Dictionary<string, object?> { ["booking_id"] = Booking, ["from_patient_id"] = Person1, ["to_patient_id"] = Person2, ["patient_code"] = "MF-100024", ["queue_number"] = "R-015", ["already"] = false });
            }
            return healthy(r);
        };
        var svc = rig.Get<IVisitReassignService>();
        var p = await svc.GetPreviewAsync(Booking);
        Assert.True(p.Succeeded, p.Error);
        Assert.Equal("MF-000001", p.Value!.Patient.PatientCode);
        Assert.Equal([Latex], WrongPatientRules.DefaultAllergyIds(p.Value));          // added during this visit = ticked; on file before = not
        Assert.Equal("Moves with it: consultation notes, prescription, payment (OR OR-0042).", WrongPatientRules.MovesSummary(p.Value)); // paid visit: payment/OR move too

        var newId = Guid.NewGuid();
        var req = new WrongPatientRequest(true, new WrongPatientNewPerson(newId, "jessie", null, "fakerubinew", SexType.Male, new DateOnly(1990, 1, 20), "0918 555 0101", null),
            null, "  Different person with a similar name  ", [Latex]);
        var outcome = await svc.ReassignAsync(Booking, req);
        Assert.True(outcome.Succeeded, outcome.Error);
        Assert.Equal(Person2, outcome.Moved!.ToPatientId);
        Assert.Equal("MF-100024", outcome.Moved.PatientCode);
        using var json = JsonDocument.Parse(posted!);
        var b = json.RootElement;
        Assert.Equal("new", b.GetProperty("mode").GetString());
        Assert.Equal(newId, b.GetProperty("new_patient").GetProperty("patient_id").GetGuid()); // retry-safe id from the dialog
        Assert.Equal("Jessie", b.GetProperty("new_patient").GetProperty("first_name").GetString());
        Assert.Equal("Fakerubinew", b.GetProperty("new_patient").GetProperty("last_name").GetString());
        Assert.Equal("1990-01-20", b.GetProperty("new_patient").GetProperty("date_of_birth").GetString());
        Assert.Equal("Different person with a similar name", b.GetProperty("reason").GetString());
        Assert.Equal(Latex, Assert.Single(b.GetProperty("move_allergy_ids").EnumerateArray()).GetGuid());
        Assert.False(b.GetProperty("acknowledge_same_day_visit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("target_patient_id").ValueKind);
    }

    [Fact]
    public async Task The_servers_409_questions_come_back_as_warnings_and_other_refusals_as_errors()
    {
        await using var rig = await Rig();
        var answer = "target_has_visit_that_day";
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            if (r.RequestUri!.AbsolutePath != $"/api/bookings/{Booking:D}/reassign-patient") return healthy(r);
            return answer switch
            {
                "target_has_visit_that_day" => FakeHandler.Json(new { error = answer, message = "Fake Persontwo already has a visit on Nov 26, 2026. Move this visit anyway?" }, HttpStatusCode.Conflict),
                "possible_duplicate" => FakeHandler.Json(new Dictionary<string, object?>
                {
                    ["error"] = answer, ["message"] = "A patient with this name and birthday is already on file.",
                    ["matches"] = new[] { new Dictionary<string, object?> { ["patient_id"] = Person2, ["patient_code"] = "MF-000002", ["first_name"] = "Jessie", ["last_name"] = "Fakerubinew", ["date_of_birth"] = "1990-01-20" } },
                }, HttpStatusCode.Conflict),
                _ => FakeHandler.Json(new { error = "visit_changed", message = "This visit was just changed by someone else. Close this window and try again." }, HttpStatusCode.Conflict),
            };
        };
        var svc = rig.Get<IVisitReassignService>();
        var existing = new WrongPatientRequest(false, null, Person2, "Wrong person picked at the desk", []);
        var w1 = await svc.ReassignAsync(Booking, existing);
        Assert.False(w1.Succeeded);
        Assert.Equal(WrongPatientWarning.SameDayVisit, w1.Warning);
        Assert.Contains("already has a visit", w1.Error);

        answer = "possible_duplicate";
        var w2 = await svc.ReassignAsync(Booking, existing with { NewPatient = true, TargetPatientId = null, Person = new WrongPatientNewPerson(Guid.NewGuid(), "Jessie", null, "Fakerubinew", SexType.Male, new DateOnly(1990, 1, 20), null, null) });
        Assert.Equal(WrongPatientWarning.PossibleDuplicate, w2.Warning);
        Assert.Equal("MF-000002", Assert.Single(w2.Matches!).PatientCode);

        answer = "visit_changed";
        var e = await svc.ReassignAsync(Booking, existing with { AcknowledgeSameDayVisit = true });
        Assert.Equal(WrongPatientWarning.None, e.Warning);
        Assert.Contains("just changed", e.Error);
    }

    [Fact]
    public async Task Offline_mode_refuses_with_needs_internet_and_sends_nothing()
    {
        await using var rig = await Rig();
        await rig.GoOfflineAsync();
        var svc = rig.Get<IVisitReassignService>();
        var p = await svc.GetPreviewAsync(Booking);
        Assert.False(p.Succeeded);
        Assert.True(AdminOnlineOnly.IsNeedsInternet(p.Error));
        var m = await svc.ReassignAsync(Booking, new WrongPatientRequest(false, null, Person2, "Wrong person picked at the desk", []));
        Assert.False(m.Succeeded);
        Assert.True(m.NeedsInternet);
        Assert.DoesNotContain(rig.Http.Requests, r => r.PathAndQuery.Contains("reassign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_unreachable_in_Online_mode_says_nothing_was_saved()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath.Contains("reassign", StringComparison.Ordinal) ? throw new HttpRequestException("connection refused") : healthy(r);
        // the app's online-write rule (OnlineFirstProxy): no local copy is made; the dialog shows this message inside itself and stays open
        var ex = await Assert.ThrowsAsync<ClinicNode.Core.Sync.OnlineWriteFailedException>(() =>
            rig.Get<IVisitReassignService>().ReassignAsync(Booking, new WrongPatientRequest(false, null, Person2, "Wrong person picked at the desk", [])));
        Assert.Contains("Nothing was saved", ex.Message);
    }

    [Fact]
    public async Task Sync_first_unsent_work_for_this_visit_stops_the_move_until_it_is_sent()
    {
        await using var rig = await Rig();
        var factory = rig.Get<IDbContextFactory<ClinicAppDbContext>>();
        Guid bookingId, otherBooking;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var ids = await db.Bookings.AsNoTracking().OrderBy(b => b.CreatedAt).Select(b => b.BookingId).Take(2).ToListAsync();
            bookingId = ids[0]; otherBooking = ids[1];
        }
        var sync = new AckingSync(factory, rig.Get<ISyncOutbox>());
        var noSync = new VisitSyncGate(factory);
        var gate = new VisitSyncGate(factory, sync);
        Assert.Null(await noSync.PendingProblemAsync(bookingId));                       // nothing waiting: the move may go ahead

        var op = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await rig.Get<ISyncOutbox>().EnqueueAsync(db, new SyncOutboxEntry { OperationId = op, Aggregate = "booking", EntityId = bookingId, OperationType = "update", PayloadJson = "{}", CreatedAt = DateTimeOffset.UtcNow });
        }
        Assert.Equal(WrongPatientRules.PendingMessage, await noSync.PendingProblemAsync(bookingId)); // unsent and no sync → refused
        Assert.Null(await noSync.PendingProblemAsync(otherBooking));                     // another visit's work does not block this one

        sync.Fail = true;                                                                  // offline / not registered: still waiting → refused
        Assert.Equal(WrongPatientRules.PendingMessage, await gate.SyncFirstAsync(bookingId));
        sync.Fail = false;                                                                 // the sync sends it → the move may go ahead
        Assert.Null(await gate.SyncFirstAsync(bookingId));
        Assert.Equal(2, sync.Runs);
    }

    [Fact]
    public void Dialog_rules_reason_new_person_and_target()
    {
        var today = new DateOnly(2026, 11, 26);
        var ok = new WrongPatientRequest(true, new WrongPatientNewPerson(Guid.NewGuid(), "Ana", null, "Cruz", SexType.Female, new DateOnly(1990, 1, 1), null, null), null, "Different person", []);
        Assert.Null(WrongPatientRules.Problem(ok, Person1, today));
        Assert.Contains("at least 5", WrongPatientRules.Problem(ok with { Reason = "no" }, Person1, today));
        Assert.Contains("Birthday", WrongPatientRules.Problem(ok with { Person = ok.Person! with { DateOfBirth = null } }, Person1, today));
        Assert.Contains("future", WrongPatientRules.Problem(ok with { Person = ok.Person! with { DateOfBirth = today.AddDays(1) } }, Person1, today));
        Assert.Contains("Last name", WrongPatientRules.Problem(ok with { Person = ok.Person! with { LastName = " " } }, Person1, today));
        var existing = new WrongPatientRequest(false, null, null, "Different person", []);
        Assert.Contains("Choose the patient", WrongPatientRules.Problem(existing, Person1, today));
        Assert.Contains("already under", WrongPatientRules.Problem(existing with { TargetPatientId = Person1 }, Person1, today));
        Assert.Null(WrongPatientRules.Problem(existing with { TargetPatientId = Person2 }, Person1, today));
        Assert.Equal(WrongPatientWarning.SameDayVisit, WrongPatientRules.WarningFor("target_has_visit_that_day"));
        Assert.Equal(WrongPatientWarning.None, WrongPatientRules.WarningFor("visit_changed"));
    }

    /// <summary>A sync that "sends" everything waiting (marks it acked), or fails like an offline computer.</summary>
    sealed class AckingSync(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox) : ISyncService
    {
        public bool Fail; public int Runs;
        public SyncStatus Current { get; } = new();
        public event Action<SyncStatus>? Changed { add { } remove { } }
        public Task<SyncStatus> RefreshStatusAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public async Task<SyncCycleResult> RunOnceAsync(CancellationToken ct = default)
        {
            Runs++;
            if (Fail) return new SyncCycleResult(false, SyncFailureKind.Offline, "offline");
            await using var db = await factory.CreateDbContextAsync(ct);
            foreach (var e in await outbox.ListPendingAsync(db, 500, ct)) await outbox.MarkAckedAsync(db, e.OperationId, ct);
            return new SyncCycleResult(true, SyncFailureKind.None, null);
        }
    }
}
//JJQR - END
