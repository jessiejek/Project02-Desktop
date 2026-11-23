//JJQR - AI (Nov 23, 2026) - web parity: opening a patient's uploaded files, the admin Sync review / merge calls and rules, and the "privacy notice shown" audit entry at registration.
using System.Net;
using System.Text;
using ClinicNode.Api.Http;
using ClinicNode.Api.Services;
using ClinicNode.Core.Admin;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Staff;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Api.Tests;

public class WebParityTests
{
    private static (ApiClient Client, FakeHandler Http) Make()
    {
        var http = new FakeHandler();
        var o = new ApiOptions { BaseUrl = () => "http://api.test/", HttpHandlerFactory = () => http };
        var tokens = new InMemoryApiTokenStore();
        tokens.Set("test-token", "refresh");
        var conn = new ApiConnectivity(o);
        return (new ApiClient(o, tokens, new ApiAuthClient(o, tokens, conn), conn), http);
    }

    // ── open an uploaded file ──
    [Fact]
    public async Task Download_sends_the_sign_in_and_returns_the_bytes_name_and_type()
    {
        var (api, http) = Make();
        http.Route = _ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-fake")) };
            res.Content.Headers.ContentType = new("application/pdf");
            res.Content.Headers.ContentDisposition = new("attachment") { FileName = "\"cbc.pdf\"" };
            return res;
        };
        var dl = await api.DownloadAsync("/api/patient-lab-results/abc/file");
        Assert.Equal("%PDF-fake", Encoding.UTF8.GetString(dl.Bytes));
        Assert.Equal("application/pdf", dl.ContentType);
        Assert.Equal("cbc.pdf", dl.FileName);
        Assert.Equal("test-token", http.Requests[0].Auth);
    }

    [Fact]
    public async Task Download_of_a_missing_file_is_a_typed_not_found()
    {
        var (api, http) = Make();
        http.Route = _ => FakeHandler.Status(HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<ApiNotFoundException>(() => api.DownloadAsync("/api/patient-documents/zzz/file"));
    }

    [Theory]
    [InlineData("lab", "patient-lab-results")]
    [InlineData("doc", "patient-documents")]
    public async Task Chart_file_opens_from_the_right_endpoint_and_is_saved_with_a_viewable_extension(string kind, string route)
    {
        var (api, http) = Make();
        http.Route = _ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            res.Content.Headers.ContentType = new("image/png");
            return res;
        };
        var id = Guid.NewGuid();
        var svc = new ApiDoctorPatientsService(api);
        var path = await svc.DownloadFileAsync(new ChartFile(id, "Chest x-ray", DateTimeOffset.UtcNow, kind));
        try
        {
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.EndsWith(".png", path);
            Assert.Contains($"/api/{route}/", http.Requests[0].PathAndQuery);
            Assert.EndsWith("/file", http.Requests[0].PathAndQuery);
        }
        finally { if (path is not null) File.Delete(path); }
    }

    // ── Sync review ──
    [Fact]
    public async Task Sync_review_list_asks_for_the_filters_and_maps_the_items()
    {
        var (api, http) = Make();
        var pid = Guid.NewGuid();
        http.Route = _ => FakeHandler.Json(new
        {
            items = new[] { new { id = Guid.NewGuid(), kind = "possible_duplicate_patient", status = "open", title = "Maria Cruz", details = new { reason = "same name and birthdate", empty = "" }, patient_id = pid, other_patient_id = Guid.NewGuid(), created_at = DateTimeOffset.UtcNow } },
            total = 1, open_total = 1, page = 1, page_size = 25,
        });
        var list = await new ApiAdminSyncReviewService(api).ListAsync("open", "possible_duplicate_patient", 1, 25);
        Assert.Contains("status=open", http.Requests[0].PathAndQuery);
        Assert.Contains("kind=possible_duplicate_patient", http.Requests[0].PathAndQuery);
        Assert.Contains("page_size=25", http.Requests[0].PathAndQuery);
        Assert.True(list.FromServer);
        Assert.Equal(1, list.OpenTotal);
        var item = Assert.Single(list.Items);
        Assert.Equal(pid, item.PatientId);
        Assert.Single(item.Details); // the empty value is dropped, like the web
        Assert.True(SyncReviewRules.IsDuplicate(item.Kind));
    }

    [Fact]
    public async Task Sync_review_resolve_and_merge_post_the_same_bodies_as_the_web()
    {
        var (api, http) = Make();
        http.Route = _ => FakeHandler.Status(HttpStatusCode.OK);
        var svc = new ApiAdminSyncReviewService(api);
        var item = Guid.NewGuid(); var keep = Guid.NewGuid(); var gone = Guid.NewGuid();
        await svc.ResolveAsync(item, "dismissed", "  Checked, different people  ");
        await svc.MergeAsync(keep, gone, " Same person ");
        Assert.Equal($"/api/sync/review-items/{item:D}/resolve", http.Requests[0].PathAndQuery);
        Assert.Contains("\"resolution\":\"dismissed\"", http.Requests[0].Body);
        Assert.Contains("\"note\":\"Checked, different people\"", http.Requests[0].Body);
        Assert.Equal("/api/patients/merge", http.Requests[1].PathAndQuery);
        Assert.Contains($"\"keep_patient_id\":\"{keep:D}\"", http.Requests[1].Body);
        Assert.Contains($"\"merge_patient_id\":\"{gone:D}\"", http.Requests[1].Body);
    }

    [Fact]
    public void Sync_review_rules_match_the_web_wording()
    {
        Assert.NotNull(SyncReviewRules.NoteProblem("abc"));
        Assert.Null(SyncReviewRules.NoteProblem("Checked the booklet"));
        Assert.NotNull(SyncReviewRules.NoteProblem(new string('x', 1001)));
        Assert.Equal("No records", SyncReviewRules.CountsText(new Dictionary<string, int>()));
        Assert.Equal("3 visits, 1 consultation, 2 allergies", SyncReviewRules.CountsText(new Dictionary<string, int> { ["bookings"] = 3, ["consultations"] = 1, ["allergies"] = 2, ["vitals"] = 0 }));
        var s = SyncReviewRules.MergeSummary("Ana Cruz", "MF-000001", "Ana Cruz", "MF-000002", new Dictionary<string, int> { ["bookings"] = 2 });
        Assert.Contains("2 visits", s);
        Assert.Contains("cannot be undone", s);
    }

    // ── privacy notice at the front desk ──
    private static QuickRegisterForm Form(bool shown) => new("Ana", null, "Cruz", new DateOnly(1990, 1, 1), SexType.Female, null, null, Guid.NewGuid(), shown);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Quick_register_records_the_notice_only_when_the_desk_ticked_it(bool shown, bool expectCall)
    {
        var (api, http) = Make();
        var patientId = Guid.NewGuid();
        http.Route = r => r.RequestUri!.PathAndQuery.EndsWith("/privacy-notice-shown")
            ? FakeHandler.Status(HttpStatusCode.NoContent)
            : FakeHandler.Json(new { patient_id = patientId, patient_code = "MF-000009", first_name = "Ana", last_name = "Cruz", date_of_birth = "1990-01-01", sex = "Female", is_guest = true });
        var (result, row) = await new ApiStaffPatientsService(api).QuickRegisterAsync(Form(shown));
        Assert.True(result.Succeeded);
        Assert.NotNull(row);
        Assert.Equal(expectCall, http.Saw($"/api/patients/{patientId:D}/privacy-notice-shown"));
    }

    [Fact]
    public async Task A_failed_notice_entry_does_not_undo_the_registration()
    {
        var (api, http) = Make();
        var patientId = Guid.NewGuid();
        http.Route = r => r.RequestUri!.PathAndQuery.EndsWith("/privacy-notice-shown")
            ? FakeHandler.Status(HttpStatusCode.InternalServerError)
            : FakeHandler.Json(new { patient_id = patientId, patient_code = "MF-000010", first_name = "Ana", last_name = "Cruz", date_of_birth = "1990-01-01", sex = "Female", is_guest = true });
        var (result, row) = await new ApiStaffPatientsService(api).QuickRegisterAsync(Form(true));
        Assert.True(result.Succeeded);
        Assert.Equal(patientId, row!.PatientId);
    }
}
//JJQR - END
