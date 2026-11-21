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
        //JJQR - AI (Nov 22, 2026) - the "Create portal account" button only led to a "not wired yet" note: removed until the flow exists //JJQR - END
        col.Children.Add(account);
        return col;
    }
    //JJQR - END

    Button Tab(string id, string label) =>
        StaffUi.Btn(label, async () => { _tab = id; await ReloadAsync(); }, _tab == id ? "PrimaryButton" : "SecondaryButton");

    View Bookings(StaffPatientDetail d)
    {
        if (d.Bookings.Count == 0) return ClinicTheme.Text("No bookings yet.", "MutedBody");
        //JJQR - AI (Nov 22, 2026) - bookings as a table (date, doctor, status); a row opens the booking
        return ClinicNode.App.Views.Shared.Controls.ClinicUi.Table(
            [
                new ClinicNode.App.Views.Shared.Controls.TableColumn("Date", new GridLength(160), o => ClinicTheme.Text(StaffUi.Date(((ClinicNode.Core.Staff.PatientBookingRow)o).AppointmentDate), "LabelLg")),
                new ClinicNode.App.Views.Shared.Controls.TableColumn("Doctor", GridLength.Star, o => ClinicTheme.Text(((ClinicNode.Core.Staff.PatientBookingRow)o).DoctorName is { Length: > 0 } dn ? dn : "—", "BodyMd")),
                new ClinicNode.App.Views.Shared.Controls.TableColumn("Status", new GridLength(150), o => ClinicTheme.MakeStatusPill(((ClinicNode.Core.Staff.PatientBookingRow)o).Status.ToString())),
            ], d.Bookings.Cast<object>(), "No bookings yet.", o => { var id = ((ClinicNode.Core.Staff.PatientBookingRow)o).BookingId; _ = StaffUi.PushAsync<StaffBookingDetailPage>(this, id); });
        //JJQR - END
    }

    static View Records(StaffPatientDetail d)
    {
        var col = new VerticalStackLayout { Spacing = 8, Children = { ClinicTheme.Text("Read-only. Only the doctor can change medical records.", "MutedSm") } };
        //JJQR - AI (Nov 22, 2026) - records as a table: date, chief complaint, diagnosis
        col.Children.Add(ClinicNode.App.Views.Shared.Controls.ClinicUi.Table(
            [
                new ClinicNode.App.Views.Shared.Controls.TableColumn("Date", new GridLength(190), o => ClinicTheme.Text(((ClinicNode.Core.Staff.PatientRecordRow)o).AppointmentDate is { } dt ? LongDate(dt) : "—", "BodyMd")),
                new ClinicNode.App.Views.Shared.Controls.TableColumn("Chief complaint", GridLength.Star, o => ClinicTheme.Text(((ClinicNode.Core.Staff.PatientRecordRow)o).ChiefComplaint is { Length: > 0 } c ? c : "No chief complaint recorded", "LabelLg")),
                new ClinicNode.App.Views.Shared.Controls.TableColumn("Diagnosis", new GridLength(320), o => ClinicNode.App.Views.Shared.Controls.ClinicUi.Cell(string.Join(", ", ((ClinicNode.Core.Staff.PatientRecordRow)o).Diagnoses))),
            ], d.Records.Cast<object>(), "No medical records yet."));
        //JJQR - END
        return col;
    }
}
//JJQR - END
