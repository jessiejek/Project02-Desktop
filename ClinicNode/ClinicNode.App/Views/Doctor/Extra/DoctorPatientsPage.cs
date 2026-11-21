//JJQR - AI (Nov 21, 2026) - D-PATIENTS: doctor patient list (web doctor/patients/page.tsx): search by name or ID, latest appointment, open chart; 20 per page.
//JJQR - AI (21 Nov 2026) - 1:1 pass against doctor/patients/page.tsx: not-linked = the "Doctor profile not linked" header only; load failure =
// ErrorState "Patient list unavailable" / "We could not load your patients. Please try again." + Try again (search and table hidden);
// "Find a patient" label + "Name or patient ID" search + "Loading patients…" / "n patients"; DataTable columns Patient (name + mono code) /
// Latest appointment / actions ("Open chart" primary, "Latest appointment" secondary); empty messages; DataTable pagination footer
// (total label, ‹ page buttons ›), always shown like the web.
//JJQR - END
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorPatientsPage : StaffLivePage
{
    private readonly Entry _search = new() { Placeholder = "Name or patient ID", MaximumWidthRequest = 448, HorizontalOptions = LayoutOptions.Start, WidthRequest = 448 };
    private readonly Label _count = ClinicTheme.Text("Loading patients…", "LabelSm");
    private readonly VerticalStackLayout _list = StaffUi.Column(0);
    private readonly VerticalStackLayout _main;
    private readonly VerticalStackLayout _root;
    private IReadOnlyList<DoctorPatientRow> _rows = [];
    private int _page = 1;
    private bool _pending = true;

    public DoctorPatientsPage(IServiceProvider sp) : base(sp, "Patient charts", live: false)
    {
        _search.TextChanged += (_, _) => { _page = 1; Render(); };
        //JJQR - AI (21 Nov 2026)
        _count.TextColor = ClinicTheme.Color("OnSurfaceVariant");
        _main = new VerticalStackLayout
        {
            Spacing = 24,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children = { ClinicTheme.Text("Find a patient", "LabelMd"), _search, _count },
                },
                _list,
            },
        };
        _root = new VerticalStackLayout
        {
            Spacing = 32,
            Children =
            {
                StaffUi.Header("Patient charts", "Your patients, their latest appointment and clinical records."),
                LoadError,
                ErrorStateView,
                _main,
            },
        };
        Content = StaffUi.Scroll(_root);
        Render();
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - web ErrorState (search + table hidden while it shows)
    protected override (string Title, string Message)? LoadErrorState => ("Patient list unavailable", "We could not load your patients. Please try again.");
    protected override void OnLoadStateChanged(bool failed) => _main.IsVisible = !failed;
    //JJQR - END

    protected override async Task LoadAsync()
    {
        var doctorId = DoctorExtraUi.DoctorId(Sp);
        if (doctorId is null)
        {
            //JJQR - AI (21 Nov 2026) - web: only the DoctorPageHeader "Doctor profile not linked"
            _root.Children.Clear();
            _root.Children.Add(StaffUi.Header("Doctor profile not linked", "Ask your clinic administrator to link your account to a doctor profile before opening patient records."));
            return;
            //JJQR - END
        }
        _pending = true;
        try
        {
            _rows = await Svc<IDoctorPatientsService>().ListAsync(doctorId.Value);
        }
        finally
        {
            _pending = false;
        }
        Render();
    }

    private void Render()
    {
        //JJQR - AI (21 Nov 2026) - web DataTable
        _list.Children.Clear();
        IReadOnlyList<DoctorPatientRow> filtered = _pending ? [] : Svc<IDoctorPatientsService>().Filter(_rows, _search.Text);
        _count.Text = _pending ? "Loading patients…" : $"{filtered.Count} patients";
        var size = DoctorExtraRules.PatientPageSize;
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)size));
        _page = Math.Clamp(_page, 1, pages);

        var columns = new List<TableColumn>
        {
            new("Patient", new GridLength(2, GridUnitType.Star), o =>
            {
                var r = (DoctorPatientRow)o;
                return new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = r.FullName, Style = ClinicTheme.Style("BodyMd"), FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap },
                        new Label { Text = r.PatientCode, Style = ClinicTheme.Style("LabelSm"), TextColor = ClinicTheme.Color("OnSurfaceVariant"), FontFamily = "Courier New" },
                    },
                };
            }),
            new("Latest appointment", new GridLength(1.2, GridUnitType.Star), o => ClinicTheme.Text(DoctorExtraRules.VisitDate(((DoctorPatientRow)o).LatestVisitDate), "BodyMd")),
            new("", GridLength.Auto, o =>
            {
                var r = (DoctorPatientRow)o;
                return StaffUi.Wrap(
                    DoctorExtraUi.Spaced(StaffUi.Btn("Open chart", () => StaffUi.PushAsync<DoctorPatientChartPage>(this, r.PatientId)), 8, 0),
                    StaffUi.Btn("Latest appointment", () => StaffUi.PushAsync<DoctorAppointmentDetailPage>(this, r.LatestBookingId), "SecondaryButton"));
            }),
        };
        var empty = _pending ? "Loading patients…"
            : !string.IsNullOrEmpty(_search.Text) ? "No patients match this search. Try another name or ID."
            : "Patients will appear here after they book with you.";
        _list.Children.Add(ClinicUi.Table(columns, filtered.Skip((_page - 1) * size).Take(size).Cast<object>(), empty));

        // pagination footer: total label, ‹ 1 2 3 ›
        var footer = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16, Padding = new Thickness(24, 16) };
        footer.Add(new Label { Text = $"{filtered.Count} patients", Style = ClinicTheme.Style("LabelSm"), TextColor = ClinicTheme.Color("OnSurfaceVariant"), VerticalOptions = LayoutOptions.Center }, 0);
        var nav = new HorizontalStackLayout { Spacing = 8 };
        var prev = StaffUi.Btn("‹", () => { _page--; Render(); return Task.CompletedTask; }, "SecondaryButton");
        prev.IsEnabled = _page > 1;
        nav.Children.Add(prev);
        for (var p = 1; p <= pages; p++)
        {
            var target = p;
            nav.Children.Add(StaffUi.Btn(p.ToString(), () => { _page = target; Render(); return Task.CompletedTask; }, p == _page ? "PrimaryButton" : "GhostButton"));
        }
        var next = StaffUi.Btn("›", () => { _page++; Render(); return Task.CompletedTask; }, "SecondaryButton");
        next.IsEnabled = _page < pages;
        nav.Children.Add(next);
        footer.Add(nav, 1);
        _list.Children.Add(footer);
        //JJQR - END
    }
}
//JJQR - END
