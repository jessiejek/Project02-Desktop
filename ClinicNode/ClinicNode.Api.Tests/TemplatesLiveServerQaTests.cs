//JJQR - AI (Nov 25, 2026) - owner: "DO CRUD on templates, all of them, QA it" without disturbing the doctor's computer. OPT-IN live QA:
// runs the desktop's REAL template service (ApiDoctorTemplatesWorkspaceService, the code behind Doctor > Templates) against a LOCAL dev server
// (Project02-be with Database:Provider=Sqlite, Development seed, fake accounts). Skipped unless CLINIC_QA_SERVER is set, e.g. http://localhost:5055.
// Never point it at the live clinic server: it creates and deletes templates.
using System.Net.Http.Json;
using System.Text.Json;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Api.Tests;

public class TemplatesLiveServerQaTests
{
    static readonly string? Server = Environment.GetEnvironmentVariable("CLINIC_QA_SERVER");

    sealed record DoctorMe(Guid DoctorId);

    static async Task<(ServiceProvider Sp, IDoctorTemplatesWorkspaceService Svc, Guid DoctorId, string Dir)> StartAsync()
    {
        Assert.False(string.IsNullOrWhiteSpace(Server));
        Assert.True(Server!.Contains("localhost", StringComparison.OrdinalIgnoreCase) || Server.Contains("127.0.0.1"), "QA only runs against a local dev server");
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-templates-qa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
        s.AddClinicNodeSync();
        s.AddClinicNodeApi(_ => Server, o => { o.AutoStart = false; });
        var sp = s.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();

        using var http = new HttpClient { BaseAddress = new Uri(Server) };
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = "doctor@clinic.test", password = DevDataSeeder.DevPassword });
        login.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        sp.GetRequiredService<IApiTokenStore>().Set(json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);

        var api = sp.GetRequiredService<IApiClient>();
        var me = await api.GetAsync<DoctorMe>("/api/doctors/me", null);
        Assert.NotNull(me);
        return (sp, new ApiDoctorTemplatesWorkspaceService(api), me!.DoctorId, dir);
    }

    static string Tag() => "QA test " + DateTime.Now.ToString("HHmmss");

    [Fact]
    public async Task Soap_templates_create_read_update_delete()
    {
        if (string.IsNullOrWhiteSpace(Server)) return; // opt-in: set CLINIC_QA_SERVER to a local dev server
        var (sp, svc, doc, dir) = await StartAsync();
        var undo = new List<Func<Task>>(); // JJQR - AI (Nov 25, 2026) - code review: clean up what this test created even when an assertion fails
        try
        {
            var name = Tag() + " SOAP";
            var r = await svc.SaveSoapAsync(doc, null, new TplSoap(Guid.Empty, name, false, "Cough", "3 days", "Clear lungs", "URTI", "Rest", "Adult"));
            Assert.True(r.Succeeded, "create: " + r.Error);
            var row = (await svc.GetWorkspaceAsync(doc)).Soap.Single(t => t.Title == name);
            undo.Add(() => svc.DeleteSoapAsync(row.Id));
            Assert.Equal("URTI", row.Assessment);

            r = await svc.SaveSoapAsync(doc, row.Id, row with { Title = name + " edited", Plan = "Fluids", Category = "Pedia" });
            Assert.True(r.Succeeded, "update: " + r.Error);
            var edited = (await svc.GetWorkspaceAsync(doc)).Soap.Single(t => t.Id == row.Id);
            Assert.Equal(name + " edited", edited.Title);
            Assert.Equal("Fluids", edited.Plan);
            Assert.Equal("Pedia", edited.Category);

            r = await svc.DeleteSoapAsync(row.Id);
            Assert.True(r.Succeeded, "delete: " + r.Error);
            Assert.DoesNotContain((await svc.GetWorkspaceAsync(doc)).Soap, t => t.Id == row.Id);
        }
        finally { foreach (var u in undo) { try { await u(); } catch { } } await sp.DisposeAsync(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Phrases_create_read_update_move_reorder_delete()
    {
        if (string.IsNullOrWhiteSpace(Server)) return; // opt-in: set CLINIC_QA_SERVER to a local dev server
        var (sp, svc, doc, dir) = await StartAsync();
        var undo = new List<Func<Task>>(); // JJQR - AI (Nov 25, 2026) - code review: clean up what this test created even when an assertion fails
        try
        {
            var label = Tag() + " phrase";
            var r = await svc.SavePhraseAsync(doc, null, "Plan", label, "Increase fluids.");
            Assert.True(r.Succeeded, "create: " + r.Error);
            var row = (await svc.GetWorkspaceAsync(doc)).Phrases.Single(p => p.Label == label);
            undo.Add(() => svc.DeletePhraseAsync(row.Id));

            r = await svc.SavePhraseAsync(doc, row.Id, "Assessment", label + " edited", "Viral illness.");
            Assert.True(r.Succeeded, "update + move: " + r.Error);
            var moved = (await svc.GetWorkspaceAsync(doc)).Phrases.Single(p => p.Id == row.Id);
            Assert.Equal("Assessment", moved.Field);
            Assert.Equal("Viral illness.", moved.Body);

            var ids = (await svc.GetWorkspaceAsync(doc)).Phrases.Where(p => p.Field == "Assessment").OrderBy(p => p.SortOrder).Select(p => p.Id).ToList();
            if (ids.Count > 1)
            {
                var reordered = ids.Where(i => i != row.Id).Prepend(row.Id).ToList();
                r = await svc.ReorderPhrasesAsync(doc, reordered);
                Assert.True(r.Succeeded, "reorder: " + r.Error);
                var first = (await svc.GetWorkspaceAsync(doc)).Phrases.Where(p => p.Field == "Assessment").OrderBy(p => p.SortOrder).First();
                Assert.Equal(row.Id, first.Id);
            }

            r = await svc.DeletePhraseAsync(row.Id);
            Assert.True(r.Succeeded, "delete: " + r.Error);
            Assert.DoesNotContain((await svc.GetWorkspaceAsync(doc)).Phrases, p => p.Id == row.Id);
        }
        finally { foreach (var u in undo) { try { await u(); } catch { } } await sp.DisposeAsync(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Prescription_sets_create_read_update_remove_line_delete()
    {
        if (string.IsNullOrWhiteSpace(Server)) return; // opt-in: set CLINIC_QA_SERVER to a local dev server
        var (sp, svc, doc, dir) = await StartAsync();
        var undo = new List<Func<Task>>(); // JJQR - AI (Nov 25, 2026) - code review: clean up what this test created even when an assertion fails
        try
        {
            var meds = await svc.ListMedicinesAsync();
            var m1 = meds.Count > 0 ? meds[0] : new TplMedicine(Guid.Empty, "PARACETAMOL 500MG TAB");
            var m2 = meds.Count > 1 ? meds[1] : new TplMedicine(Guid.Empty, "CETIRIZINE 10MG TAB");
            TplRxLine Line(TplMedicine m, string sig) => new(m.MedicineId, m.GenericName, "500 mg", "10 tablets", sig, false, null, null, null, null, null, null, null);
            var name = Tag() + " Rx";
            var r = await svc.SaveRxSetAsync(doc, null, name, [Line(m1, "1 tab every 8 hours"), Line(m2, "1 tab at night")]);
            Assert.True(r.Succeeded, "create: " + r.Error);
            var row = (await svc.GetWorkspaceAsync(doc)).Rx.Single(t => t.Title == name);
            undo.Add(() => svc.DeleteRxSetAsync(row.Id));
            Assert.Equal(2, row.Lines.Count);

            r = await svc.SaveRxSetAsync(doc, row.Id, name + " edited", [row.Lines[1] with { Instruction = "1 tab twice a day" }]); // the owner's case: Remove a medicine, then Save
            Assert.True(r.Succeeded, "update (remove a line): " + r.Error);
            var edited = (await svc.GetWorkspaceAsync(doc)).Rx.Single(t => t.Id == row.Id);
            Assert.Equal(name + " edited", edited.Title);
            Assert.Single(edited.Lines);
            Assert.Equal("1 tab twice a day", edited.Lines[0].Instruction);

            r = await svc.DeleteRxSetAsync(row.Id);
            Assert.True(r.Succeeded, "delete: " + r.Error);
            Assert.DoesNotContain((await svc.GetWorkspaceAsync(doc)).Rx, t => t.Id == row.Id);
        }
        finally { foreach (var u in undo) { try { await u(); } catch { } } await sp.DisposeAsync(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Favorites_create_read_update_reorder_delete()
    {
        if (string.IsNullOrWhiteSpace(Server)) return; // opt-in: set CLINIC_QA_SERVER to a local dev server
        var (sp, svc, doc, dir) = await StartAsync();
        var undo = new List<Func<Task>>(); // JJQR - AI (Nov 25, 2026) - code review: clean up what this test created even when an assertion fails
        try
        {
            var meds = await svc.ListMedicinesAsync();
            // JJQR - AI (Nov 25, 2026) - code review: pick a medicine that is not a favorite yet (a duplicate could be refused)
            var already = (await svc.GetWorkspaceAsync(doc)).Favorites.Select(f => f.GenericName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pick = meds.LastOrDefault(m => !already.Contains(m.GenericName));
            var name = pick?.GenericName ?? "QA TEST MEDICINE " + Tag();
            var medId = pick?.MedicineId ?? Guid.Empty;
            var before = (await svc.GetWorkspaceAsync(doc)).Favorites.Select(f => f.Id).ToHashSet();
            var r = await svc.SaveFavoriteAsync(doc, null, new TplFavorite(Guid.Empty, medId, name, "500 mg", "21 tabs", "1 tab every 8 hours", 0));
            Assert.True(r.Succeeded, "create: " + r.Error);
            var row = (await svc.GetWorkspaceAsync(doc)).Favorites.Single(f => !before.Contains(f.Id));
            undo.Add(() => svc.DeleteFavoriteAsync(row.Id));

            r = await svc.SaveFavoriteAsync(doc, row.Id, row with { Dosage = "250 mg", Instruction = "1 tab twice a day" });
            Assert.True(r.Succeeded, "update: " + r.Error);
            var edited = (await svc.GetWorkspaceAsync(doc)).Favorites.Single(f => f.Id == row.Id);
            Assert.Equal("250 mg", edited.Dosage);

            var ids = (await svc.GetWorkspaceAsync(doc)).Favorites.OrderBy(f => f.SortOrder).Select(f => f.Id).ToList();
            if (ids.Count > 1)
            {
                r = await svc.ReorderFavoritesAsync(doc, ids.Where(i => i != row.Id).Prepend(row.Id).ToList());
                Assert.True(r.Succeeded, "reorder: " + r.Error);
                Assert.Equal(row.Id, (await svc.GetWorkspaceAsync(doc)).Favorites.OrderBy(f => f.SortOrder).First().Id);
            }

            r = await svc.DeleteFavoriteAsync(row.Id);
            Assert.True(r.Succeeded, "delete: " + r.Error);
            Assert.DoesNotContain((await svc.GetWorkspaceAsync(doc)).Favorites, f => f.Id == row.Id);
        }
        finally { foreach (var u in undo) { try { await u(); } catch { } } await sp.DisposeAsync(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Certificate_templates_create_read_update_delete()
    {
        if (string.IsNullOrWhiteSpace(Server)) return; // opt-in: set CLINIC_QA_SERVER to a local dev server
        var (sp, svc, doc, dir) = await StartAsync();
        var undo = new List<Func<Task>>(); // JJQR - AI (Nov 25, 2026) - code review: clean up what this test created even when an assertion fails
        try
        {
            var name = Tag() + " cert";
            var r = await svc.SaveCertAsync(doc, null, new TplCert(Guid.Empty, name, false, "Acute gastroenteritis", "Rest for 2 days", null));
            Assert.True(r.Succeeded, "create: " + r.Error);
            var row = (await svc.GetWorkspaceAsync(doc)).Certificates.Single(t => t.Title == name);
            undo.Add(() => svc.DeleteCertAsync(row.Id));

            r = await svc.SaveCertAsync(doc, row.Id, row with { Title = name + " edited", Recommendations = "Rest for 3 days", PurposeException = "School" });
            Assert.True(r.Succeeded, "update: " + r.Error);
            var edited = (await svc.GetWorkspaceAsync(doc)).Certificates.Single(t => t.Id == row.Id);
            Assert.Equal("Rest for 3 days", edited.Recommendations);
            Assert.Equal("School", edited.PurposeException);

            r = await svc.DeleteCertAsync(row.Id);
            Assert.True(r.Succeeded, "delete: " + r.Error);
            Assert.DoesNotContain((await svc.GetWorkspaceAsync(doc)).Certificates, t => t.Id == row.Id);
        }
        finally { foreach (var u in undo) { try { await u(); } catch { } } await sp.DisposeAsync(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Restore_built_ins_works_for_every_kind()
    {
        if (string.IsNullOrWhiteSpace(Server)) return; // opt-in: set CLINIC_QA_SERVER to a local dev server
        var (sp, svc, doc, dir) = await StartAsync();
        var undo = new List<Func<Task>>(); // JJQR - AI (Nov 25, 2026) - code review: clean up what this test created even when an assertion fails
        try
        {
            foreach (var kind in new[] { "soap", "rx", "cert", "phrase" })
            {
                var r = await svc.RestoreBuiltInsAsync(doc, kind);
                Assert.True(r.Succeeded, $"restore {kind}: " + r.Error);
            }
        }
        finally { foreach (var u in undo) { try { await u(); } catch { } } await sp.DisposeAsync(); try { Directory.Delete(dir, true); } catch { } }
    }
}
//JJQR - END
