//JJQR - AI (Nov 21, 2026) - /staff/patients: search (name, code, phone, email), 25 per page newest first, quick register (MF- code), open patient.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffPatientsPage : StaffLivePage
{
    const int PageSize = 25;
    private readonly Entry _search = new() { Placeholder = "Name, patient code, phone or email" };
    private readonly VerticalStackLayout _list = new() { Spacing = 0 };
    private readonly Label _range = ClinicTheme.Text("", "MutedSm");
    private readonly Label _pageLabel = ClinicTheme.Text("", "MutedSm");
    private readonly Button _prev;
    private readonly Button _next;
    private int _page = 1;
    private int _pageCount = 1;
    private CancellationTokenSource? _debounce;
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/patients): <ErrorState title="Patients unavailable"/> replaces the list + pager (header and search stay)
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    protected override (string Title, string Message)? LoadErrorState => ("Patients unavailable", "The patient list could not be loaded. Check the connection to the clinic server, then try again.");
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffPatientsPage(IServiceProvider sp) : base(sp, "Patients", live: false)
    {
        _search.TextChanged += async (_, _) =>
        {
            _debounce?.Cancel();
            var cts = _debounce = new CancellationTokenSource();
            try { await Task.Delay(250, cts.Token); } catch (TaskCanceledException) { return; }
            _page = 1;
            await ReloadAsync();
        };
        _prev = StaffUi.Btn("Previous", async () => { if (_page > 1) { _page--; await ReloadAsync(); } }, "SecondaryButton");
        _next = StaffUi.Btn("Next", async () => { if (_page < _pageCount) { _page++; await ReloadAsync(); } }, "SecondaryButton");
        //JJQR - AI (21 Nov 2026) - STAFF PARITY
        _body.Children.Add(_list);
        _body.Children.Add(StaffUi.Wrap(_range, _prev, _pageLabel, _next));
        //JJQR - END
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Patients", "Find a patient to check their details, visits and portal account.",
                    //JJQR - AI (21 Nov 2026) - web header has only "New walk-in"; registering a new patient lives in the walk-in wizard (QuickRegisterPage)
                    StaffUi.Btn("New walk-in", () => Shell.Current.GoToAsync("//staff-walk-in"), "SecondaryButton")),
                    //JJQR - END
                StaffUi.Field("Search patients", _search),
                LoadError,
                //JJQR - AI (21 Nov 2026) - STAFF PARITY: error state; web pager is one row: "1–25 of N" · Previous · Page X of Y · Next
                ErrorStateView,
                _body,
                //JJQR - END
            }
        });
    }

    protected override async Task LoadAsync()
    {
        var res = await Svc<IStaffPatientsService>().SearchAsync(_search.Text, _page, PageSize);
        _pageCount = res.PageCount;
        //JJQR - AI (Nov 22, 2026) - patients as a table: name + code, age / sex, phone, portal account, Open
        _list.Children.Clear();
        _list.Children.Add(ClinicUi.Table(
            [
                new TableColumn("Patient", GridLength.Star, o => { var p = (StaffPatientRow)o; var id = p.PatientId; return StaffUi.TwoLine(p.FullName, p.PatientCode, () => StaffUi.PushAsync<StaffPatientDetailPage>(this, id)); }),
                new TableColumn("Age / sex", new GridLength(130), o => { var p = (StaffPatientRow)o; return ClinicTheme.Text($"{AgeText(p.DateOfBirth)} · {p.Sex}", "BodyMd"); }),
                new TableColumn("Phone", new GridLength(160), o => ClinicUi.Cell(((StaffPatientRow)o).ContactNumber)),
                new TableColumn("Portal account", new GridLength(190), o => ClinicTheme.MakeStatusPill(((StaffPatientRow)o).AccountStatus)),
                new TableColumn("", new GridLength(120), o => { var id = ((StaffPatientRow)o).PatientId; return StaffUi.Btn("Open", () => StaffUi.PushAsync<StaffPatientDetailPage>(this, id), "SecondaryButton"); }),
            ], res.Items.Cast<object>(), string.IsNullOrWhiteSpace(_search.Text) ? "No patients registered yet." : $"No patients match “{_search.Text.Trim()}”."));
        //JJQR - END
        _range.Text = $"{res.RangeStart}–{res.RangeEnd} of {res.TotalCount}";
        _pageLabel.Text = $"  Page {res.Page} of {res.PageCount}  ";
        //JJQR - AI (21 Nov 2026) - STAFF PARITY: web disables Previous on page 1 and Next on the last page
        _prev.IsEnabled = res.Page > 1;
        _next.IsEnabled = res.Page < res.PageCount;
        //JJQR - END
    }

    static string AgeText(DateOnly dob)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var years = today.Year - dob.Year - (today < dob.AddYears(today.Year - dob.Year) ? 1 : 0);
        if (years >= 2) return $"{years}y";
        var months = (today.Year - dob.Year) * 12 + today.Month - dob.Month - (today.Day < dob.Day ? 1 : 0);
        return $"{Math.Max(0, months)}mo";
    }

    async Task RegisterAsync()
    {
        var created = await QuickRegisterPage.ShowAsync(this, Svc<IStaffPatientsService>());
        if (created is null) return;
        await StaffUi.PushAsync<StaffPatientDetailPage>(this, created.PatientId);
    }
}
//JJQR - END
