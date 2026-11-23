//JJQR - AI (Nov 21, 2026) - reusable WalkInWizard view (plain UI). Host: `new WalkInWizardView(services)` inside any page (staff-walk-in, admin-walk-in).
//JJQR - AI (21 Nov 2026) - 1:1 pass against Project02-fe components/walkin/WalkInWizard.tsx (used by /staff/walk-in and /admin/walk-in):
//  loading ("Loading patients…") / "Patients unavailable" + Try again; StepIndicator Patient · Confirm;
//  step 1 card "Who is checking in?" + "Register new patient", "Search patients", the patient table (Patient with radio + code, Contact, Sex,
//  Age, Date of birth, Account pill), empty text + "Register them as a new patient", "a–b of n" + Previous / "p of N" / Next, and the bottom
//  bar "Selected: name" / "Select a patient to continue." + Continue (hosts can dock it with ContinueBar + DockContinueBar);
//  step 2 "Confirm the visit": identity box, Visit type segmented New visit / Follow-up, Discount None / Senior citizen / PWD + hints,
//  "Medical certificate requested", "Pay at clinic" box, error, Back + "Check in and get queue number" ("Checking in…");
//  done card: "<name> is checked in", Queue number, priority pill, code, visit · provisional fee, Print slip / Next walk-in, "View booking details".
//JJQR - END
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Print;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class WalkInWizardView : ContentView
{
    private readonly IServiceProvider _sp;
    private readonly Entry _search = new() { Placeholder = "Name, patient code, phone or email" };
    private CancellationTokenSource? _debounce;
    //JJQR - AI (21 Nov 2026) - bottom bar (web: fixed to the bottom on step 1)
    private readonly ContentView _bar = new();
    private bool _dockBar;
    //JJQR - END

    public WalkInWizardViewModel Vm { get; }

    /// <summary>Optional: called with the booking id when the desk taps "View booking details" on the done screen.</summary>
    public Func<Guid, Task>? OpenBooking { get; set; }

    //JJQR - AI (21 Nov 2026)
    /// <summary>The step-1 "Selected: … / Continue" bar. Place it outside the page's ScrollView and set <see cref="DockContinueBar"/>.</summary>
    public View ContinueBar => _bar;

    /// <summary>true = the host shows <see cref="ContinueBar"/> itself (docked); false = it is drawn under the step-1 card.</summary>
    public bool DockContinueBar
    {
        get => _dockBar;
        set
        {
            _dockBar = value;
            if (value && _bar.Parent is Layout old) old.Children.Remove(_bar); // the host adopts it
            Render();
        }
    }

    /// <summary>Raised after each re-render so the host can hide its page header on the done card (web shows only the card there).</summary>
    public event Action<WalkInStep>? StepRendered;
    //JJQR - END

    public WalkInWizardView(IServiceProvider sp)
    {
        _sp = sp;
        Vm = StaffUi.Get<WalkInWizardViewModel>(sp);
        Vm.Changed += () => MainThread.BeginInvokeOnMainThread(Render);
        _search.TextChanged += async (_, e) =>
        {
            _debounce?.Cancel();
            var cts = _debounce = new CancellationTokenSource();
            try { await Task.Delay(250, cts.Token); } catch (TaskCanceledException) { return; }
            await Vm.SearchAsync(e.NewTextValue, 1); // web: back to page 1 whenever the search changes
        };
        Render();
    }

    /// <summary>Call from the host page's OnAppearing.</summary>
    public async Task LoadAsync()
    {
        if (Vm.Step != WalkInStep.Patient) return;
        await Vm.SearchAsync(_search.Text, Vm.Page);
    }

    Page? FindPage()
    {
        Element? e = this;
        while (e is not null && e is not Page) e = e.Parent;
        return e as Page;
    }

    void Render()
    {
        //JJQR - AI (21 Nov 2026) - web: loading / loadError replace the wizard (header stays on the host page)
        if (Vm.Step == WalkInStep.Patient && Vm.Results is null)
            Content = Vm.LoadFailed ? LoadError() : ClinicTheme.Text("Loading patients…", "MutedBody");
        else
            Content = Vm.Step switch
            {
                WalkInStep.Confirm => Wrap(Step2()),
                WalkInStep.Done => Done(),
                _ => Wrap(Step1()),
            };
        RenderBar();
        StepRendered?.Invoke(Vm.Step);
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - web ErrorState
    View LoadError() => StaffUi.Card(
        ClinicTheme.Text(WalkInWizardViewModel.LoadFailedTitle, "HeadlineSm"),
        ClinicTheme.Text(WalkInWizardViewModel.LoadFailedMessage, "MutedBody"),
        StaffUi.Btn("Try again", Vm.RetryLoadAsync, "SecondaryButton"));

    /// <summary>StepIndicator + the step card (+ the bar when the host does not dock it).</summary>
    View Wrap(View card)
    {
        var col = new VerticalStackLayout { Spacing = 16, Children = { Stepper(), card } };
        if (Vm.Step == WalkInStep.Patient && !_dockBar)
        {
            if (_bar.Parent is Layout old) old.Children.Remove(_bar); // one parent at a time (the previous render's column)
            col.Children.Add(_bar);
        }
        if (Vm.Step == WalkInStep.Confirm) col.MaximumWidthRequest = 896; // web max-w-doctor-record
        return col;
    }

    /// <summary>web StepIndicator(["Patient", "Confirm"]): number (✓ when done) + label, the current step highlighted.</summary>
    View Stepper()
    {
        var current = Vm.Step == WalkInStep.Patient ? 1 : 2;
        var row = new HorizontalStackLayout { Spacing = 12 };
        string[] steps = ["Patient", "Confirm"];
        for (var i = 0; i < steps.Length; i++)
        {
            var n = i + 1;
            if (i > 0) row.Children.Add(new BoxView { WidthRequest = 32, HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), VerticalOptions = LayoutOptions.Center });
            var done = n < current;
            var on = n == current;
            var dot = new Border
            {
                WidthRequest = 28, HeightRequest = 28, StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                BackgroundColor = ClinicTheme.Color(done || on ? "Primary" : "SurfaceContainerHigh"),
                Content = new Label
                {
                    Text = done ? "✓" : n.ToString(), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    TextColor = ClinicTheme.Color(done || on ? "OnPrimary" : "OnSurfaceVariant"), FontSize = 13,
                },
            };
            row.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { dot, new Label { Text = steps[i], Style = ClinicTheme.Style(on ? "LabelLg" : "MutedSm"), VerticalOptions = LayoutOptions.Center } } });
        }
        return row;
    }

    /// <summary>web StatusPill for the account column.</summary>
    static View AccountPill(string status) => status switch
    {
        "LinkedAccount" => ClinicUi.Badge("Linked account", "Success"),
        "NoAccount" => ClinicUi.Badge("No account", "Neutral"),
        _ => ClinicUi.Badge("Account unknown", "Warning"),
    };

    async Task RegisterAsync()
    {
        if (FindPage() is not { } host) return;
        var created = await QuickRegisterPage.ShowAsync(host, StaffUi.Get<IStaffPatientsService>(_sp));
        if (created is not null) Vm.Registered(created);
    }

    void RenderBar()
    {
        _bar.IsVisible = Vm.Step == WalkInStep.Patient && Vm.Results is not null;
        if (!_bar.IsVisible) { _bar.Content = null; return; }
        var text = new Label { Style = ClinicTheme.Style("MutedBody"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        if (Vm.Patient is { } p)
            text.FormattedText = new FormattedString { Spans = { new Span { Text = "Selected: " }, new Span { Text = p.FullName, FontAttributes = FontAttributes.Bold, TextColor = ClinicTheme.Color("OnSurface") } } };
        else
            text.Text = "Select a patient to continue.";
        var go = StaffUi.Btn("Continue  ›", () => { Vm.Continue(); return Task.CompletedTask; });
        go.MinimumWidthRequest = 192;
        go.IsEnabled = Vm.Patient is not null;
        var grid = new Grid
        {
            Padding = 16, ColumnSpacing = 16,
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        grid.Add(text, 0);
        grid.Add(go, 1);
        _bar.Content = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") }, grid },
        };
    }
    //JJQR - END

    View Step1()
    {
        //JJQR - AI (21 Nov 2026) - web step 1 card
        var res = Vm.Results!;
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        head.Add(new Label { Text = "Who is checking in?", Style = ClinicTheme.Style("HeadlineMd"), VerticalOptions = LayoutOptions.End }, 0);
        head.Add(StaffUi.Btn("⊕ Register new patient", RegisterAsync, "SecondaryButton"), 1);

        View list;
        if (res.Items.Count == 0)
        {
            list = new Border
            {
                Style = ClinicTheme.Style("TableFrame"),
                Content = new VerticalStackLayout
                {
                    Spacing = 8, Padding = new Thickness(16, 24),
                    Children =
                    {
                        new Label { Text = Vm.Query.Length > 0 ? $"No patients match “{Vm.Query}”." : "No patients registered yet.", Style = ClinicTheme.Style("MutedBody"), HorizontalTextAlignment = TextAlignment.Center },
                        new HorizontalStackLayout { HorizontalOptions = LayoutOptions.Center, Children = { StaffUi.Btn("Register them as a new patient", RegisterAsync, "GhostButton") } },
                    },
                },
            };
        }
        else
        {
            var today = ClinicClock.Today;
            var selectedId = Vm.Patient?.PatientId;
            var columns = new List<TableColumn>
            {
                new("Patient", new GridLength(2.4, GridUnitType.Star), o =>
                {
                    var p = (StaffPatientRow)o;
                    var radio = new RadioButton { GroupName = "walkin-patient", IsChecked = p.PatientId == selectedId, VerticalOptions = LayoutOptions.Center };
                    radio.CheckedChanged += (_, e) => { if (e.Value && Vm.Patient?.PatientId != p.PatientId) Vm.Pick(p); };
                    return new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            radio,
                            new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { ClinicTheme.Text(p.FullName, "LabelLg"), new Label { Text = p.PatientCode, Style = ClinicTheme.Style("MutedSm"), FontFamily = "Courier New" } } },
                        },
                    };
                }),
                new("Contact", new GridLength(1.3, GridUnitType.Star), o => ClinicUi.Cell(((StaffPatientRow)o).ContactNumber)),
                new("Sex", new GridLength(0.8, GridUnitType.Star), o => ClinicUi.Cell(((StaffPatientRow)o).Sex.ToString())),
                new("Age", new GridLength(0.6, GridUnitType.Star), o => new Label { Text = StaffRules.AgeOn(((StaffPatientRow)o).DateOfBirth, today).ToString(), Style = ClinicTheme.Style("TableCell"), HorizontalTextAlignment = TextAlignment.End }),
                new("Date of birth", new GridLength(1.2, GridUnitType.Star), o => ClinicUi.Cell(StaffUi.Date(((StaffPatientRow)o).DateOfBirth))),
                new("Account", new GridLength(1.3, GridUnitType.Star), o => AccountPill(((StaffPatientRow)o).AccountStatus)),
            };
            //JJQR - AI (Nov 23, 2026) - owner: highlight the chosen patient and dim the others (still clickable). The old tint here looked for one grid per row, but the table is a single grid, so nothing was ever tinted.
            var table = ClinicUi.Table(columns, res.Items.Cast<object>(), onTap: o => Vm.Pick((StaffPatientRow)o), isSelected: o => ((StaffPatientRow)o).PatientId == selectedId);
            //JJQR - END
            list = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new ContentView { MinimumWidthRequest = 672, Content = table } };
        }

        var error = StaffUi.Error();
        StaffUi.Show(error, Vm.Error);
        var card = new List<View> { head, StaffUi.Field("Search patients", _search), error, list };
        if (res.Items.Count > 0)
        {
            var pager = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
            pager.Add(new Label { Text = $"{res.RangeStart}–{res.RangeEnd} of {res.TotalCount}", Style = ClinicTheme.Style("MutedSm"), VerticalOptions = LayoutOptions.Center }, 0);
            var prev = StaffUi.Btn("‹ Previous", Vm.PrevPageAsync, "SecondaryButton");
            prev.IsEnabled = res.Page > 1;
            var next = StaffUi.Btn("Next ›", Vm.NextPageAsync, "SecondaryButton");
            next.IsEnabled = res.Page < res.PageCount;
            pager.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                Children = { prev, new Label { Text = $"{res.Page} of {res.PageCount}", Style = ClinicTheme.Style("MutedSm"), VerticalOptions = LayoutOptions.Center }, next },
            }, 1);
            card.Add(pager);
        }
        return StaffUi.Card(card.ToArray());
        //JJQR - END
    }

    View Step2()
    {
        //JJQR - AI (21 Nov 2026) - web step 2 card "Confirm the visit"
        var p = Vm.Patient!;
        var identity = new Border
        {
            Style = ClinicTheme.Style("ListCard"),
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = p.FullName, Style = ClinicTheme.Style("HeadlineSm"), LineBreakMode = LineBreakMode.WordWrap },
                    ClinicTheme.Text($"{p.PatientCode} · {p.Sex} · {Vm.PatientAge} yrs", "MutedSm"),
                },
            },
        };

        //JJQR - AI (Nov 21, 2026) - "Confirm the visit" as a proper two-panel layout: LEFT = who + payment note, RIGHT = the visit options; buttons normal-sized at the bottom
        var visitHost = new ContentView();
        void RenderVisit() => visitHost.Content = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(
            [StaffRules.VisitTypeLongLabel(VisitType.New), StaffRules.VisitTypeLongLabel(VisitType.FollowUp)],
            Vm.VisitType == VisitType.FollowUp ? 1 : 0,
            i => { Vm.VisitType = i == 1 ? VisitType.FollowUp : VisitType.New; RenderVisit(); });
        RenderVisit();
        visitHost.HorizontalOptions = LayoutOptions.Start;

        var autoHint = ClinicTheme.Text("", "FieldHint");
        var laneHint = ClinicTheme.Text("", "FieldHint");
        void Hints()
        {
            StaffUi.Show(autoHint, Vm.DiscountAutoHint);
            laneHint.Text = Vm.DiscountLaneHint;
        }
        var discountHost = new ContentView { HorizontalOptions = LayoutOptions.Start };
        void RenderDiscount() => discountHost.Content = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(
            ["None", "Senior citizen", "PWD"],
            Vm.Discount switch { "Senior" => 1, "PWD" => 2, _ => 0 },
            i => { Vm.Discount = i switch { 1 => "Senior", 2 => "PWD", _ => null }; RenderDiscount(); Hints(); });
        RenderDiscount();
        Hints();

        var cert = new CheckBox { IsChecked = Vm.MedCertRequested };
        cert.CheckedChanged += (_, e) => Vm.MedCertRequested = e.Value;
        var certRow = ClinicPickers.CheckRow(cert, "Medical certificate requested", "Adds the certificate fee to this visit.");

        var initials = string.Concat(p.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        var avatar = new Border
        {
            WidthRequest = 64, HeightRequest = 64, StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("PrimarySubtle"), HorizontalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 32 },
            Content = new Label { Text = initials, FontFamily = ClinicTheme.FontSemiBold, FontSize = 22, TextColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var who = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(22),
            VerticalOptions = LayoutOptions.Start,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    avatar,
                    new Label { Text = p.FullName, FontFamily = ClinicTheme.FontSemiBold, FontSize = 20, LineBreakMode = LineBreakMode.WordWrap },
                    ClinicTheme.Text($"{p.PatientCode} · {p.Sex} · {Vm.PatientAge} yrs", "MutedBody"),
                    new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), Margin = new Thickness(0, 8) },
                    ClinicTheme.Text("PAYMENT", "LabelSm"),
                    new Label { Text = "Pay at clinic", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15 },
                    ClinicTheme.Text("The fee is estimated now and finalised at the consultation.", "MutedSm"),
                },
            },
        };

        var options = new VerticalStackLayout
        {
            Spacing = 20,
            Children =
            {
                StaffUi.Field("Visit type", visitHost),
                StaffUi.Field("Discount", new VerticalStackLayout { Spacing = 6, Children = { discountHost, autoHint, laneHint } }),
                StaffUi.Field("Extras", certRow),
            },
        };
        var cols = new Grid { ColumnSpacing = 28, ColumnDefinitions = { new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(3, GridUnitType.Star)) } };
        cols.Add(who, 0, 0);
        cols.Add(options, 1, 0);

        var back = StaffUi.Btn("‹ Back", () => { Vm.Back(); return Task.CompletedTask; }, "SecondaryButton");
        back.IsEnabled = !Vm.Busy;
        var checkIn = StaffUi.Btn(Vm.Busy ? "Checking in…" : "Check in and get queue number", () => Vm.CheckInAsync());
        checkIn.IsEnabled = !Vm.Busy;
        var footer = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        footer.Add(back, 0, 0);
        footer.Add(checkIn, 2, 0);

        var items = new List<View>
        {
            new Label { Text = "Confirm the visit", FontFamily = ClinicTheme.FontSemiBold, FontSize = 20 },
            cols,
        };
        if (Vm.Error is { } err) items.Add(ClinicUi.Alert(err, success: false));
        items.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
        items.Add(footer);
        var card = StaffUi.Card(items.ToArray());
        card.HorizontalOptions = LayoutOptions.Center;
        card.WidthRequest = 920;
        return card;
        //JJQR - END
    }

    View Done()
    {
        var t = Vm.Ticket!;
        //JJQR - AI (21 Nov 2026) - start the write-through as soon as the ticket shows, so "Print slip" / reprint find the row at once
        _ = ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(_sp, t.BookingId, withPayment: false);
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - web done card (centered, max-w-auth)
        Label Center(string text, string style) => new() { Text = text, Style = ClinicTheme.Style(style), HorizontalTextAlignment = TextAlignment.Center };
        var ticket = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Center(string.IsNullOrWhiteSpace(t.QueueNumber) ? "No queue number yet (the clinic server gives one when this computer syncs)" : "Queue number", "MutedSm"),
                new Label { Text = string.IsNullOrWhiteSpace(t.QueueNumber) ? "Paper queue" : t.QueueNumber, Style = ClinicTheme.Style("DisplaySm"), TextColor = ClinicTheme.Color("Primary"), HorizontalTextAlignment = TextAlignment.Center },
            },
        };
        if (t.IsPriority)
            ticket.Children.Add(new HorizontalStackLayout { HorizontalOptions = LayoutOptions.Center, Children = { ClinicUi.Badge("Priority lane · Senior / PWD", "Info") } });
        ticket.Children.Add(new Label { Text = t.PatientCode, Style = ClinicTheme.Style("MutedSm"), FontFamily = "Courier New", HorizontalTextAlignment = TextAlignment.Center });
        ticket.Children.Add(Center($"{StaffRules.VisitTypeLongLabel(t.VisitType)} · provisional fee {StaffRules.Peso(t.ProvisionalFee)}", "MutedSm"));
        var ticketBox = new Border { Style = ClinicTheme.Style("ListCard"), BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Padding = 24, Content = ticket };

        var print = StaffUi.Btn("⎙ Print slip", async () =>
        {
            //JJQR - AI (21 Nov 2026) - online walk-in: copy the new booking (+ patient) into SQLite before the slip reads it
            await ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(_sp, t.BookingId, withPayment: false);
            //JJQR - END
            if (FindPage() is { } host) await PrintActions.RunAsync(host, () => StaffUi.Get<IPrintDocumentFactory>(_sp).QueueSlipAsync(t.BookingId));
        }, "SecondaryButton");
        print.HorizontalOptions = LayoutOptions.Fill;
        var next = StaffUi.Btn("Next walk-in  ›", async () => { _search.Text = ""; await Vm.ResetAsync(); });
        next.HorizontalOptions = LayoutOptions.Fill;
        var actions = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 16 };
        actions.Add(print, 0);
        actions.Add(next, 1);

        var col = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "✓", FontSize = 32, TextColor = ClinicTheme.Color("StatusSuccess"), HorizontalTextAlignment = TextAlignment.Center },
                Center($"{t.PatientName} is checked in", "HeadlineMd"),
                ticketBox,
                actions,
            },
        };
        if (OpenBooking is { } open)
        {
            var link = StaffUi.Link("View booking details", () => open(t.BookingId));
            link.HorizontalOptions = LayoutOptions.Center;
            col.Children.Add(link);
        }
        return new Border { Style = ClinicTheme.Style("Card"), MaximumWidthRequest = 448, HorizontalOptions = LayoutOptions.Center, Content = col };
        //JJQR - END
    }
}
//JJQR - END
