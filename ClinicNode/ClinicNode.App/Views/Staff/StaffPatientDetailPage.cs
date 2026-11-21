//JJQR - AI (Nov 21, 2026) - /staff/patients/[id]: Overview / Bookings (n) / Medical records (n), read-only.
using ClinicNode.App.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffPatientDetailPage : StaffLivePage
{
    private readonly Guid _id;
    private readonly ContentView _body = new();
    private string _tab = "overview";
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/patients/[id]): ErrorState "This patient didn’t load" only while nothing is loaded (a failed
    // refresh after that keeps the page silently); a missing patient = Next's 404 text; tabs and their content sit in ONE card; "Create portal account"
    // opens an inline note with Close (not a popup); header line is code · sex · Born <date> (both required on this computer); a whole booking row opens the booking.
    private bool _createAccountOpen;
    protected override (string Title, string Message)? LoadErrorState => ("This patient didn’t load", "Check the connection to the clinic server, then try again.");
    protected override string? StaleLoadWarning => "";
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffPatientDetailPage(IServiceProvider sp, Guid patientId) : base(sp, "Patient", live: false)
    {
        _id = patientId;
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children = { StaffUi.Link("‹ All patients", () => Navigation.PopAsync()), LoadError, ErrorStateView, _body }, //JJQR - AI (21 Nov 2026) - + ErrorStateView //JJQR - END
        });
    }

    //JJQR - AI (21 Nov 2026) - STAFF PARITY: web formatClinicDate ("Sat, Nov 21, 2026") / formatClinicDateShort ("Nov 21, 2026")
    static string LongDate(DateOnly d) => d.ToString("ddd, MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    //JJQR - END

    protected override async Task LoadAsync()
    {
        var d = await Svc<IStaffPatientsService>().GetDetailAsync(_id);
        //JJQR - AI (21 Nov 2026) - STAFF PARITY: the web calls notFound() (Next's default 404 page)
        if (d is null) { _body.Content = new VerticalStackLayout { Children = { ClinicTheme.Text("404", "HeadlineSm"), ClinicTheme.Text("This page could not be found.", "MutedBody") } }; return; }
        //JJQR - END
        var p = d.Patient;
        var tabs = StaffUi.Wrap(
            Tab("overview", "Overview"), Tab("bookings", $"Bookings ({d.Bookings.Count})"), Tab("records", $"Medical records ({d.Records.Count})"));
        View content = _tab switch
        {
            "bookings" => Bookings(d),
            "records" => Records(d),
            _ => Overview(p),
        };
        //JJQR - AI (21 Nov 2026) - STAFF PARITY: header line "CODE · Sex · Born <date>"
        var line = p.PatientCode;
        line += $" · {p.Sex}"; // Sex and date of birth are required on this computer, so both always show
        line += $" · Born {LongDate(p.DateOfBirth)}";
        _body.Content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                ClinicTheme.Text(p.FullName, "HeadlineLg"),
                ClinicTheme.Text(line, "MutedBody"),
                StaffUi.Card(tabs, content), // web: <Card><Tabs/>{tab content}</Card>
            }
        };
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - STAFF PARITY: Overview = Contact number, Email, then a separated "Portal account <pill>" row with the button on the right;
    // the button opens the web's inline note + Close.
    View Overview(StaffPatientRow p)
    {
        var col = new VerticalStackLayout { Spacing = 12 };
        col.Children.Add(StaffUi.Field("Contact number", ClinicTheme.Text(p.ContactNumber.Length > 0 ? p.ContactNumber : "—", "BodyMd")));
        col.Children.Add(StaffUi.Field("Email", ClinicTheme.Text(p.Email.Length > 0 ? p.Email : "—", "BodyMd")));
        col.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
        var account = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        account.Add(new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { ClinicTheme.Text("Portal account", "MutedSm"), ClinicTheme.MakeStatusPill(p.AccountStatus) } }, 0, 0);
        if (p.AccountStatus == "NoAccount" && !_createAccountOpen)
            account.Add(StaffUi.Btn("Create portal account", async () => { _createAccountOpen = true; await ReloadAsync(); }, "SecondaryButton"), 1, 0);
        col.Children.Add(account);
        if (p.AccountStatus == "NoAccount" && _createAccountOpen)
            col.Children.Add(StaffUi.Card(
                ClinicTheme.Text("Portal account creation from this page is not wired yet. Use the admin/staff invite flow when available.", "MutedBody"),
                StaffUi.Btn("Close", async () => { _createAccountOpen = false; await ReloadAsync(); }, "SecondaryButton")));
        return col;
    }
    //JJQR - END

    Button Tab(string id, string label) =>
        StaffUi.Btn(label, async () => { _tab = id; await ReloadAsync(); }, _tab == id ? "PrimaryButton" : "SecondaryButton");

    View Bookings(StaffPatientDetail d)
    {
        if (d.Bookings.Count == 0) return ClinicTheme.Text("No bookings yet.", "MutedBody");
        var col = new VerticalStackLayout();
        foreach (var b in d.Bookings)
        {
            var id = b.BookingId;
            //JJQR - AI (21 Nov 2026) - STAFF PARITY: the whole row is the link (date, doctor, status pill), like the web <Link> row
            var row = StaffUi.Row(
                ClinicTheme.Text(StaffUi.Date(b.AppointmentDate), "HeadlineSm"),
                ClinicTheme.Text(b.DoctorName.Length > 0 ? b.DoctorName : "—", "MutedSm"),
                ClinicTheme.MakeStatusPill(b.Status.ToString()));
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await StaffUi.PushAsync<StaffBookingDetailPage>(this, id);
            row.GestureRecognizers.Add(tap);
            col.Children.Add(row);
            //JJQR - END
        }
        return col;
    }

    static View Records(StaffPatientDetail d)
    {
        var col = new VerticalStackLayout { Spacing = 8, Children = { ClinicTheme.Text("Read-only. Only the doctor can change medical records.", "MutedSm") } };
        if (d.Records.Count == 0) { col.Children.Add(ClinicTheme.Text("No medical records yet.", "MutedBody")); return col; }
        foreach (var r in d.Records)
        {
            var row = new List<View>
            {
                ClinicTheme.Text(r.AppointmentDate is { } dt ? LongDate(dt) : "—", "MutedSm"), //JJQR - AI (21 Nov 2026) - web formatClinicDate //JJQR - END
                ClinicTheme.Text(r.ChiefComplaint.Length > 0 ? r.ChiefComplaint : "No chief complaint recorded", "HeadlineSm"),
            };
            if (r.Diagnoses.Count > 0) row.Add(ClinicTheme.Text(string.Join(", ", r.Diagnoses), "MutedBody"));
            col.Children.Add(StaffUi.Row(row.ToArray()));
        }
        return col;
    }
}
//JJQR - END
