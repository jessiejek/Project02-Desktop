//JJQR - AI (Nov 21, 2026) - D-APPTS: doctor appointments (web doctor/appointments/page.tsx). Today queue / history toggle, status stats + filter, search, 20 per page, row action opens the consultation or the appointment.
//JJQR - AI (21 Nov 2026) - 1:1 pass against doctor/appointments/page.tsx + components/doctor/appointments (AppointmentFocus, AppointmentList, model.ts):
//  DoctorPageHeader (title, "<date> · Your patient queue and consultations" / history description, Today | History toggle);
//  error banner with "Try again" (+ "Showing the last loaded appointments." when rows are on screen); summary line Waiting / In progress /
//  Completed ("—" until loaded); AppointmentFocus (Queue | "Continue your consultation" / "Next in today's queue", name, status, action;
//  or "No patients waiting right now" + Review appointment history); list section "Patient queue" / "All appointments", "n shown of m
//  appointments" / "Loading appointments…", Refresh ("Refreshing…"), search, status filter chips; table Queue / Patient / Visit / Status /
//  Payment / action (Resume / Open consultation / View); loading, unavailable and empty states (Clear filters, Review appointment history);
//  footer "Updated h:mm PHT · Auto-refresh every minute" + "p / N" paging. Live refresh on PatientCheckedIn / QueueUpdated / PaymentUpdated.
//JJQR - END
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Doctor.Extra;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorAppointmentsPage : StaffLivePage
{
    private readonly DoctorAppointmentsViewModel _vm = new();
    private readonly VerticalStackLayout _body = StaffUi.Column(24);
    private readonly Entry _search = new() { Placeholder = "Search patient or queue number" };
    private bool _notLinked;

    public DoctorAppointmentsPage(IServiceProvider sp) : base(sp, "Appointments")
    {
        _search.TextChanged += (_, e) => { _vm.Search = e.NewTextValue ?? ""; _vm.Page = 1; Render(); };
        Content = StaffUi.Scroll(new VerticalStackLayout { Spacing = 16, Children = { LoadError, _body } });
    }

    //JJQR - AI (21 Nov 2026) - web useClinicHubEvent("PatientCheckedIn" | "QueueUpdated" | "PaymentUpdated", load)
    protected override IReadOnlyList<string> HubEvents =>
        [ClinicNode.Api.Live.ClinicHubEvents.PatientCheckedIn, ClinicNode.Api.Live.ClinicHubEvents.QueueUpdated, ClinicNode.Api.Live.ClinicHubEvents.PaymentUpdated];
    //JJQR - END

    protected override async Task LoadAsync()
    {
        var doctorId = DoctorExtraUi.DoctorId(Sp);
        if (doctorId is null)
        {
            _notLinked = true;
            _body.Children.Clear();
            _body.Children.Add(DoctorExtraUi.NotLinked());
            return;
        }
        _notLinked = false;
        var scope = _vm.Scope;
        _vm.Fetching = true;
        if (_body.Children.Count > 0) Render();
        try
        {
            var rows = await Svc<IDoctorAppointmentsService>().ListAsync(doctorId.Value, scope);
            if (scope != _vm.Scope) return; // a newer scope won (web requestId)
            _vm.SetRows(rows);
            _vm.Error = null;
        }
        catch
        {
            if (scope == _vm.Scope) _vm.Error = DoctorAppointmentsViewModel.RefreshFailed;
        }
        finally
        {
            if (scope == _vm.Scope) _vm.Fetching = false;
        }
        Render();
    }

    private Task SwitchScope(AppointmentScope s)
    {
        if (_vm.Scope == s) return Task.CompletedTask;
        _vm.SetScope(s);
        _search.Text = "";
        Render();
        return ReloadAsync();
    }

    //JJQR - AI (21 Nov 2026) - DoctorPageHeader: headline + description, actions on the right (wrap on narrow windows)
    private static View Header(string title, string description, View? actions = null)
    {
        var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.SpaceBetween, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start };
        flex.Children.Add(new VerticalStackLayout { Spacing = 4, MaximumWidthRequest = 720, Children = { ClinicTheme.Text(title, "HeadlineLg"), ClinicTheme.Text(description, "MutedBody") } });
        if (actions is not null) flex.Children.Add(actions);
        return flex;
    }

    private static Label Muted(string text, string style = "MutedSm") => ClinicTheme.Text(text, style);
    //JJQR - END

    private void Render()
    {
        if (_notLinked) return;
        _body.Children.Clear();
        var today = _vm.Scope == AppointmentScope.Today;
        var loaded = _vm.Loaded;

        // ── header + Today | History ─────────────────────────────────────
        var toggle = new HorizontalStackLayout { Spacing = 4 };
        foreach (var (scope, label) in new[] { (AppointmentScope.Today, "Today"), (AppointmentScope.All, "History") })
        {
            var s = scope;
            toggle.Children.Add(StaffUi.Btn(label, () => SwitchScope(s), _vm.Scope == s ? "SegmentedButtonSelected" : "SegmentedButton"));
        }
        _body.Children.Add(Header(_vm.Title,
            today ? $"{DoctorExtraRules.VisitDate(ClinicApp.Domain.ClinicClock.Today)} · Your patient queue and consultations" : "Review your previous and upcoming visits, with the date of every appointment.",
            new Border { Style = ClinicTheme.Style("SegmentedControl"), Content = toggle }));

        // ── error banner ─────────────────────────────────────────────────
        if (_vm.Error is { } error)
        {
            var banner = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
            banner.Add(new Label { Text = error + (loaded ? " Showing the last loaded appointments." : ""), Style = ClinicTheme.Style("BodyMd"), TextColor = ClinicTheme.Color("StatusDanger"), VerticalOptions = LayoutOptions.Center }, 0);
            var retry = StaffUi.Btn(_vm.Fetching ? "Trying again…" : "Try again", ReloadAsync, "SecondaryButton");
            retry.IsEnabled = !_vm.Fetching;
            banner.Add(retry, 1);
            _body.Children.Add(new Border
            {
                Padding = 16, BackgroundColor = ClinicTheme.Color("StatusDangerSurface"), Stroke = ClinicTheme.Color("StatusDangerBorder"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = banner,
            });
        }

        // ── summary: Waiting / In progress / Completed ───────────────────
        var summary = new HorizontalStackLayout { Spacing = 32 };
        foreach (var (label, status) in new[] { ("Waiting", BookingStatus.CheckedIn), ("In progress", BookingStatus.InProgress), ("Completed", BookingStatus.Completed) })
            summary.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    ClinicTheme.Text(loaded ? _vm.Count(status).ToString() : "—", "HeadlineMd"),
                    new Label { Text = label, Style = ClinicTheme.Style("MutedBody"), VerticalOptions = LayoutOptions.End },
                },
            });
        _body.Children.Add(new VerticalStackLayout { Spacing = 16, Children = { summary, new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") } } });

        // ── AppointmentFocus (today, no error, loading or rows) ──────────
        if (today && _vm.Error is null && (_vm.Loading || _vm.All.Count > 0))
            _body.Children.Add(Focus());

        // ── list section ─────────────────────────────────────────────────
        _body.Children.Add(ListSection());
    }

    //JJQR - AI (21 Nov 2026) - web AppointmentFocus
    private View Focus()
    {
        View content;
        if (_vm.Loading) content = ClinicNode.App.Controls.Skeleton.Card(3); //JJQR - AI (Nov 22, 2026) - skeleton //JJQR - END
        else if (_vm.Focus is { } f)
        {
            var (_, label) = DoctorExtraRules.ActionFor(f.Status);
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 24 };
            var queue = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { Muted("Queue", "LabelSm"), new Label { Text = f.QueueNumber ?? "—", Style = ClinicTheme.Style("HeadlineLg"), TextColor = ClinicTheme.Color("Primary") } },
            };
            grid.Add(queue, 0);
            grid.Add(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    Muted(f.Status == BookingStatus.InProgress ? "Continue your consultation" : "Next in today's queue", "LabelMd"),
                    new Label { Text = f.PatientName, Style = ClinicTheme.Style("HeadlineLg"), LineBreakMode = LineBreakMode.WordWrap },
                    new HorizontalStackLayout { Children = { ClinicTheme.MakeStatusPill(f.Status.ToString()) } },
                },
            }, 1);
            var go = StaffUi.Btn(label + "  →", () => DoctorExtraUi.OpenConsultAsync(f.BookingId));
            go.VerticalOptions = LayoutOptions.Center;
            grid.Add(go, 2);
            content = grid;
        }
        else
        {
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
            grid.Add(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    ClinicTheme.Text("No patients waiting right now", "HeadlineMd"),
                    Muted("Checked-in patients will appear here. You can still review appointments below or look through previous visits.", "MutedBody"),
                },
            }, 0);
            var history = StaffUi.Btn("Review appointment history  →", () => SwitchScope(AppointmentScope.All), "SecondaryButton");
            history.VerticalOptions = LayoutOptions.Center;
            grid.Add(history, 1);
            content = grid;
        }
        return new Border { Style = ClinicTheme.Style("Card"), Content = content };
    }

    /// <summary>web list section: heading, Refresh, search, filters, table / states, footer.</summary>
    private View ListSection()
    {
        var today = _vm.Scope == AppointmentScope.Today;
        var shown = _vm.Filtered;
        var col = new VerticalStackLayout { Spacing = 16 };

        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
        head.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                ClinicTheme.Text(_vm.ListTitle, "HeadlineMd"),
                Muted(_vm.Loaded ? $"{shown.Count} shown of {_vm.All.Count} appointments" : "Loading appointments…", "LabelMd"),
            },
        }, 0);
        var refresh = StaffUi.Btn(_vm.Fetching ? "Refreshing…" : "⟳ Refresh", ReloadAsync, "SecondaryButton");
        refresh.IsEnabled = !_vm.Fetching;
        refresh.VerticalOptions = LayoutOptions.Center;
        head.Add(refresh, 1);
        col.Children.Add(head);

        _search.MaximumWidthRequest = 448;
        _search.HorizontalOptions = LayoutOptions.Start;
        _search.WidthRequest = 448;
        if (_search.Parent is Layout old) old.Children.Remove(_search);
        col.Children.Add(_search);

        var filters = StaffUi.Wrap();
        foreach (var (id, label) in DoctorExtraRules.Filters)
        {
            var f = id;
            filters.Children.Add(DoctorExtraUi.Spaced(DoctorExtraUi.Chip(label, _vm.Filter == f, () => { _vm.Filter = f; _vm.Page = 1; Render(); }), 4, 4));
        }
        col.Children.Add(filters);

        // body: loading / unavailable / table / empty
        if (_vm.Loading)
            col.Children.Add(ClinicNode.App.Controls.Skeleton.Rows(6)); //JJQR - AI (Nov 22, 2026) - skeleton //JJQR - END
        else if (_vm.Error is not null && !_vm.Loaded)
            col.Children.Add(Muted("Appointments are unavailable. Use Try again above to reload them.", "MutedBody"));
        else if (shown.Count > 0)
            col.Children.Add(Table(today));
        else
        {
            var (title, body) = DoctorExtraRules.EmptyState(!string.IsNullOrWhiteSpace(_vm.Search), _vm.All.Count, _vm.Scope);
            var empty = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16, 24) };
            empty.Children.Add(new Label { Text = "🗓", FontSize = 26, HorizontalTextAlignment = TextAlignment.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
            empty.Children.Add(new Label { Text = title, Style = ClinicTheme.Style("HeadlineSm"), HorizontalTextAlignment = TextAlignment.Center });
            empty.Children.Add(new Label { Text = body, Style = ClinicTheme.Style("MutedBody"), HorizontalTextAlignment = TextAlignment.Center });
            if (_vm.HasFilters)
                empty.Children.Add(Centered(StaffUi.Btn("Clear filters", () => { _vm.ClearFilters(); _search.Text = ""; Render(); return Task.CompletedTask; }, "SecondaryButton")));
            if (_vm.All.Count == 0 && today)
                empty.Children.Add(Centered(StaffUi.Btn("Review appointment history  →", () => SwitchScope(AppointmentScope.All))));
            col.Children.Add(empty);
        }

        // footer (once loaded): updated time + paging
        if (_vm.Loaded)
        {
            var footer = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
            footer.Add(new Label
            {
                Text = _vm.UpdatedAt is { } u ? $"Updated {DoctorExtraUi.Pht(u)} PHT · Auto-refresh every minute" : "",
                Style = ClinicTheme.Style("MutedSm"), VerticalOptions = LayoutOptions.Center,
            }, 0);
            if (_vm.PageCount > 1)
            {
                var page = _vm.CurrentPage;
                var prev = StaffUi.Btn("Previous", () => { _vm.Page = page - 1; Render(); return Task.CompletedTask; }, "SecondaryButton");
                prev.IsEnabled = page > 1;
                var next = StaffUi.Btn("Next", () => { _vm.Page = page + 1; Render(); return Task.CompletedTask; }, "SecondaryButton");
                next.IsEnabled = page < _vm.PageCount;
                footer.Add(new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { prev, new Label { Text = $"{page} / {_vm.PageCount}", Style = ClinicTheme.Style("LabelMd"), VerticalOptions = LayoutOptions.Center }, next },
                }, 1);
            }
            col.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
            col.Children.Add(footer);
        }
        return new Border { Style = ClinicTheme.Style("Card"), Content = col };
    }

    private static View Centered(View v) { v.HorizontalOptions = LayoutOptions.Center; return v; }

    /// <summary>web AppointmentList table: Queue / Patient / Visit / Status / Payment / action.</summary>
    private View Table(bool today)
    {
        var columns = new List<TableColumn>
        {
            new("Queue", new GridLength(0.8, GridUnitType.Star), o => ClinicTheme.Text(((AppointmentRow)o).QueueNumber ?? "—", "LabelLg")),
            new("Patient", new GridLength(2.2, GridUnitType.Star), o =>
            {
                var r = (AppointmentRow)o;
                var c = new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = r.PatientName, Style = ClinicTheme.Style("LabelLg"), LineBreakMode = LineBreakMode.WordWrap } } };
                if (!string.IsNullOrWhiteSpace(r.PatientCode)) c.Children.Add(new Label { Text = r.PatientCode, Style = ClinicTheme.Style("MutedSm"), FontFamily = "Courier New" });
                return c;
            }),
            new("Visit", new GridLength(1.2, GridUnitType.Star), o =>
            {
                var r = (AppointmentRow)o;
                var c = new VerticalStackLayout { Spacing = 2 };
                if (!today) c.Children.Add(ClinicTheme.Text(DoctorExtraRules.VisitDate(r.AppointmentDate), "BodyMd"));
                c.Children.Add(Muted(r.VisitType, "LabelMd"));
                return c;
            }),
            new("Status", new GridLength(1.2, GridUnitType.Star), o => new HorizontalStackLayout { Children = { ClinicTheme.MakeStatusPill(((AppointmentRow)o).Status.ToString()) } }),
            new("Payment", new GridLength(1.1, GridUnitType.Star), o => Muted(((AppointmentRow)o).PaymentStatus ?? "Not recorded", "LabelMd")),
            new("", GridLength.Auto, o =>
            {
                var r = (AppointmentRow)o;
                var (opensConsult, _) = DoctorExtraRules.ActionFor(r.Status);
                var text = r.Status == BookingStatus.InProgress ? "Resume" : r.Status == BookingStatus.CheckedIn ? "Open consultation" : "View";
                return StaffUi.Btn(text + "  →", () => opensConsult ? DoctorExtraUi.OpenConsultAsync(r.BookingId) : StaffUi.PushAsync<DoctorAppointmentDetailPage>(this, r.BookingId), "SecondaryButton");
            }),
        };
        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = new ContentView { MinimumWidthRequest = 760, Content = ClinicUi.Table(columns, _vm.Visible.Cast<object>()) },
        };
    }
    //JJQR - END
}
//JJQR - END
