//JJQR - AI (Nov 23, 2026) - offline audit: "the doctor opens every screen offline". Calls every read-only operation of every app service in Offline mode with no clinic server, with plausible ids, and reports the ones that fail.
// A read that throws here would show a blank or "could not load" screen offline. Operations that are internet-only on purpose answer with the app's own "needs an internet connection" sentence and are not failures.
using System.Reflection;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ClinicNode.Tests;

public class OfflineReadSmokeTests(ITestOutputHelper output)
{
    static readonly string[] ReadPrefixes = ["Get", "List", "Search", "Find", "Count", "Load", "Query", "Read", "Lookup", "Fetch", "Preview", "Build"];

    [Fact]
    public async Task Every_read_a_doctor_can_open_works_offline_without_a_server()
    {
        await using var host = await AppTestHost.StartAsync();
        var auth = host.Get<IAuthService>();
        var mode = host.Get<IWorkModeService>();
        await mode.InitializeAsync();
        var doctor = await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(doctor.Succeeded, doctor.Error);
        await host.Get<ClinicNode.Sync.Auth.INodeCredentialStore>().SaveAsync(new ClinicNode.Sync.Auth.NodeCredential(Guid.NewGuid(), "test-secret"));
        Assert.True((await mode.SetAsync(WorkMode.Offline)).Succeeded);

        // some real data to read: a patient with a visit that has a consultation
        var factory = host.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicAppDbContext>>();
        await using (var seed = await factory.CreateDbContextAsync())
            foreach (var day in Enumerable.Range(0, 8).Select(n => ClinicApp.Domain.ClinicClock.Today.AddDays(n)))
                foreach (var lane in ClinicNode.Sync.Local.QueueBlocks.Lanes)
                    await ClinicNode.Sync.Local.QueueBlocks.SaveAsync(seed, new ClinicNode.Sync.QueueBlockReservation(day, lane, 1, 200, 200, null, null));
        var doctorId = doctor.Session!.DoctorId!.Value;
        var (reg, patient) = await host.Get<IStaffPatientsService>().QuickRegisterAsync(new QuickRegisterForm("Smoke", null, "Test", new DateOnly(1990, 1, 1), SexType.Female, "09171234567", "Cebu"));
        Assert.True(reg.Succeeded, reg.Error);
        var walk = await host.Get<IQueueService>().CheckInWalkInAsync(new WalkInRequest(patient!.PatientId, doctorId));
        Assert.True(walk.Succeeded, walk.Error);
        var bookingId = walk.Ticket!.BookingId;
        await host.Get<IQueueService>().CallAsync(bookingId);
        var consultId = (await host.Get<ClinicNode.Core.Clinical.IConsultationService>().LoadByBookingAsync(bookingId)).Draft.ConsultationId ?? Guid.NewGuid();

        object? Arg(ParameterInfo p) => p.ParameterType == typeof(Guid) ? Pick(p.Name ?? "")
            : p.ParameterType == typeof(Guid?) ? Pick(p.Name ?? "")
            : p.ParameterType == typeof(string) ? ""
            : p.ParameterType == typeof(DateOnly) ? ClinicApp.Domain.ClinicClock.Today
            : p.ParameterType == typeof(DateOnly?) ? ClinicApp.Domain.ClinicClock.Today
            : p.ParameterType == typeof(DateTimeOffset) ? DateTimeOffset.UtcNow
            : p.ParameterType == typeof(int) ? (p.HasDefaultValue ? p.DefaultValue : 1)
            : p.ParameterType == typeof(bool) ? (p.HasDefaultValue ? p.DefaultValue : false)
            : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
            : p.HasDefaultValue ? p.DefaultValue : NoArg;
        Guid Pick(string n) => n.Contains("doctor", StringComparison.OrdinalIgnoreCase) ? doctorId
            : n.Contains("patient", StringComparison.OrdinalIgnoreCase) ? patient!.PatientId
            : n.Contains("consult", StringComparison.OrdinalIgnoreCase) ? consultId
            : bookingId;

        var failures = new List<string>(); var ok = 0; var skipped = 0;
        var assembly = typeof(IStaffPatientsService).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t.IsInterface && t.IsPublic && t.Name.EndsWith("Service")).OrderBy(t => t.Name))
        {
            object? svc;
            try { svc = host.Services.GetService(type); } catch { continue; }
            if (svc is null) continue;
            foreach (var m in type.GetMethods().Where(m => ReadPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)) && !m.IsGenericMethod && typeof(Task).IsAssignableFrom(m.ReturnType)))
            {
                var args = m.GetParameters().Select(Arg).ToArray();
                if (args.Any(a => ReferenceEquals(a, NoArg))) { skipped++; continue; }
                try { await (Task)m.Invoke(svc, args)!; ok++; }
                catch (TargetInvocationException tie) { Record(failures, type, m, tie.InnerException ?? tie); }
                catch (Exception ex) { Record(failures, type, m, ex); }
            }
        }
        output.WriteLine($"reads ok={ok}, skipped(complex arguments)={skipped}, failed={failures.Count}");
        foreach (var f in failures) output.WriteLine("FAIL " + f);
        Assert.True(failures.Count == 0, "reads that failed offline:\n" + string.Join("\n", failures));
    }

    static readonly object NoArg = new();

    static void Record(List<string> failures, Type t, MethodInfo m, Exception ex)
    {
        // the app's own sentences for things that are internet-only on purpose are not failures
        var msg = ex.Message ?? "";
        if (ex is ClinicNode.Api.Http.ApiOfflineException || msg.Contains("internet", StringComparison.OrdinalIgnoreCase) || msg.Contains("Not available in Offline mode", StringComparison.OrdinalIgnoreCase)) return;
        failures.Add($"{t.Name}.{m.Name} -> {ex.GetType().Name}: {msg.Split('\n')[0]}");
    }
}
//JJQR - END
