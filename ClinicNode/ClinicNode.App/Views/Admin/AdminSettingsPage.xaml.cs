//JJQR - AI (21 Nov 2026) - web /admin/settings layout (see AdminSettingsPage.xaml)
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public partial class AdminSettingsPage : ContentPage
{
    static readonly (string Id, string Label)[] Tabs =
    [
        ("general", "General"), ("hours", "Hours"), ("fees", "Fees"),
        ("payments", "Payments"), ("privacy", "Privacy & consent"), ("branding", "Branding"), ("printing", "Printing"), //JJQR - AI (Nov 24, 2026) //JJQR - END
    ];

    readonly AdminSettingsViewModel _vm;
    string _tab = "general";
    bool _saving;
    bool _bumpConfirm;
    string _busyRequest = "";
    string? _logoPreview;
    string? _faviconPreview;

    public AdminSettingsPage(AdminSettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render();
        //JJQR - AI (21 Nov 2026) - online-only settings: know up front whether saving is possible
        await _vm.RefreshOnlineAsync();
        //JJQR - END
        await _vm.LoadWebAsync();
        Render();
    }

    void Render()
    {
        Root.Children.Clear();
        Root.Children.Add(ClinicUi.PageHeader("Clinic settings", "Details patients see on the public page, opening hours, fees and payment options."));
        if (_vm.LoadFailed)
        {
            Root.Children.Add(ClinicTheme.Text("Settings unavailable", "HeadlineSm"));
            Root.Children.Add(ClinicTheme.Text("Clinic settings could not be loaded. Nothing has been changed; try again.", "MutedBody"));
            Root.Children.Add(ClinicUi.Button("Try again", async () => { await _vm.LoadWebAsync(); Render(); }, ButtonKind.Secondary));
            return;
        }
        if (!_vm.Loaded) { Root.Children.Add(new ActivityIndicator { IsRunning = true }); return; }
        //JJQR - AI (21 Nov 2026) - Jessie: settings and fees are online-only
        if (!_vm.CanSaveOnline)
            Root.Children.Add(new Border
            {
                Style = ClinicTheme.Style("PillWarning"), Padding = new Thickness(12, 8),
                Content = ClinicTheme.Text(AdminSettingsViewModel.NeedsInternetNote, "PillLabelWarning"),
            });
        //JJQR - END

        var tabs = new HorizontalStackLayout { Spacing = 4 };
        foreach (var (id, label) in Tabs)
        {
            var b = new Button { Text = label, Style = ClinicTheme.Style(_tab == id ? "SegmentedButtonSelected" : "SegmentedButton") };
            var captured = id;
            b.Clicked += (_, _) => ClinicNode.App.Controls.UiTurn.Next(() => { _tab = captured; _vm.SaveError = ""; Render(); }); //JJQR - AI (Nov 24, 2026) - Windows: redraw on the next UI turn //JJQR - END
            tabs.Children.Add(b);
        }
        var body = new VerticalStackLayout { Spacing = 16 };
        var card = new VerticalStackLayout { Spacing = 24, Children = { new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = tabs } } };
        if (!string.IsNullOrWhiteSpace(_vm.SaveError)) card.Children.Add(ClinicUi.Alert(_vm.SaveError, success: false));
        card.Children.Add(body);
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = card });

        switch (_tab)
        {
            case "general": General(body); break;
            case "hours": HoursTab(body); break;
            case "fees": Fees(body); break;
            case "payments": Payments(body); break;
            case "privacy": Privacy(body); break;
            case "branding": Branding(body); break;
            case "printing": Printing(body); break; //JJQR - AI (Nov 24, 2026) //JJQR - END
        }
    }

    // ---- helpers ----
    static Entry Box(string value, Action<string> set, Keyboard? kb = null, string? placeholder = null)
    {
        var e = new Entry { Text = value, Keyboard = kb ?? Keyboard.Default, Placeholder = placeholder };
        e.TextChanged += (_, a) => set(a.NewTextValue ?? "");
        return e;
    }

    static Editor Area(string value, Action<string> set, int rows)
    {
        var e = new Editor { Text = value, HeightRequest = rows * 24 + 16, AutoSize = EditorAutoSizeOption.Disabled };
        e.TextChanged += (_, a) => set(a.NewTextValue ?? "");
        return e;
    }

    static View Warning(string text) =>
        new Border { Style = ClinicTheme.Style("PillWarning"), Padding = new Thickness(12, 8), Content = ClinicTheme.Text(text, "PillLabelWarning") };

    View SaveBar(Func<Task<string?>> save, string label = "Save changes", bool disabled = false) => SaveBar(save, label, disabled, out _);

    View SaveBar(Func<Task<string?>> save, string label, bool disabled, out Button saveButton)
    {
        var button = ClinicUi.Button(_saving ? "Saving…" : label, async () =>
        {
            //JJQR - AI (21 Nov 2026) - online-only: re-check right before saving; offline nothing is saved or queued
            await _vm.RefreshOnlineAsync();
            if (!_vm.CanSaveOnline) { _vm.SaveError = ClinicNode.Core.Admin.OnlineOnlyAdminSettingsService.NeedsInternet; Render(); return; }
            //JJQR - END
            _saving = true; _vm.SaveError = ""; Render();
            var error = await save();
            _saving = false;
            if (error is null) ClinicToast.Show($"Settings saved at {DateTime.Now:h:mm:ss tt}.", ToastKind.Success);
            else _vm.SaveError = error;
            Render();
        });
        button.IsEnabled = !disabled && !_saving && _vm.CanSaveOnline; //JJQR - AI (21 Nov 2026) online-only //JJQR - END
        saveButton = button;
        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") },
                new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Children = { button } },
            },
        };
    }

    static Grid TwoCols(View a, View b)
    {
        var g = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        g.Add(a, 0, 0); g.Add(b, 1, 0);
        return g;
    }

    // ---- tabs ----
    void General(VerticalStackLayout body)
    {
        var warn = new ContentView();
        void Check() => warn.Content = _vm.GeneralInvalid ? Warning("Clinic name and address cannot be empty.") : null;
        var save = SaveBar(() => _vm.SaveWebAsync("Clinic details could not be saved. Your changes are still here; try again."), "Save changes", _vm.GeneralInvalid, out var saveButton);
        void Refresh() { Check(); saveButton.IsEnabled = !_vm.GeneralInvalid && !_saving && _vm.CanSaveOnline; }
        body.Children.Add(warn);
        body.Children.Add(ClinicUi.Field("Clinic name", Box(_vm.ClinicName, v => { _vm.ClinicName = v; Refresh(); })));
        body.Children.Add(ClinicUi.Field("Address", Box(_vm.Address, v => { _vm.Address = v; Refresh(); }), "Shown on the public home page under \u201cFind us\u201d."));
        body.Children.Add(TwoCols(
            ClinicUi.Field("Contact number", Box(_vm.ContactNumber, v => _vm.ContactNumber = v, Keyboard.Telephone)),
            ClinicUi.Field("Email", Box(_vm.Email, v => _vm.Email = v, Keyboard.Email))));
        body.Children.Add(ClinicUi.Field("Description", Area(_vm.Description, v => _vm.Description = v, 3)));
        body.Children.Add(save);
        Check();
    }

    void HoursTab(VerticalStackLayout body)
    {
        var list = new VerticalStackLayout();
        var warn = new ContentView();
        var save = SaveBar(_vm.SaveHoursWebAsync, "Save hours", _vm.InvalidHour is not null, out var saveButton);
        void Check()
        {
            var bad = _vm.InvalidHour;
            warn.Content = bad is null ? null : Warning($"{bad.DayName}: closing time must be after opening time.");
            saveButton.IsEnabled = bad is null && !_saving && _vm.CanSaveOnline;
        }
        // web order: Sunday first
        foreach (var h in _vm.Hours.OrderBy(x => x.DayOfWeek))
        {
            var row = new Grid
            {
                ColumnSpacing = 16, Padding = new Thickness(0, 8),
                ColumnDefinitions = { new ColumnDefinition(new GridLength(112)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            };
            var day = ClinicTheme.Text(h.DayName, "LabelLg"); day.VerticalOptions = LayoutOptions.Center;
            row.Add(day, 0, 0);
            var middle = new ContentView { VerticalOptions = LayoutOptions.Center };
            void Middle()
            {
                if (h.IsClosed) { middle.Content = ClinicTheme.Text("Closed", "MutedBody"); return; }
                var open = new TimePicker { Time = h.Open, WidthRequest = 136 };
                var close = new TimePicker { Time = h.Close, WidthRequest = 136 };
                open.TimeSelected += (_, e) => { if (e.NewTime is { } t) h.Open = t; Check(); };
                close.TimeSelected += (_, e) => { if (e.NewTime is { } t) h.Close = t; Check(); };
                var dash = ClinicTheme.Text("–", "MutedBody"); dash.VerticalOptions = LayoutOptions.Center;
                middle.Content = new HorizontalStackLayout { Spacing = 8, Children = { ClinicPickers.TimeBox(null, open), dash, ClinicPickers.TimeBox(null, close) } };
            }
            Middle();
            row.Add(middle, 1, 0);
            var closed = new CheckBox { IsChecked = h.IsClosed, Color = ClinicTheme.Color("Primary") };
            closed.CheckedChanged += (_, e) => { h.IsClosed = e.Value; Middle(); Check(); };
            var closedLabel = ClinicTheme.Text("Closed", "LabelMd"); closedLabel.VerticalOptions = LayoutOptions.Center;
            row.Add(new HorizontalStackLayout { Spacing = 4, Children = { closed, closedLabel } }, 2, 0);
            if (list.Children.Count > 0) list.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
            list.Children.Add(row);
        }
        body.Children.Add(list);
        body.Children.Add(warn);
        body.Children.Add(save);
        Check();
    }

    void Fees(VerticalStackLayout body)
    {
        body.Children.Add(ClinicTheme.Text("Flat clinic-wide fee schedule (contract §16.6). The doctor picks which line applies during the consultation; the booking total is recomputed on save.", "FieldHint"));
        var warn = new ContentView();
        //JJQR - AI (21 Nov 2026) - web saveFees: only the fee lines are sent
        var save = SaveBar(_vm.SaveFeesWebAsync, "Save fees", _vm.FeesInvalid, out var saveButton);
        //JJQR - END
        void Check() { warn.Content = _vm.FeesInvalid ? Warning("Fees cannot be negative.") : null; saveButton.IsEnabled = !_vm.FeesInvalid && !_saving && _vm.CanSaveOnline; }
        body.Children.Add(warn);

        var list = new VerticalStackLayout();
        void Line(View label, View input)
        {
            var g = new Grid { Padding = new Thickness(0, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            label.VerticalOptions = LayoutOptions.Center;
            g.Add(label, 0, 0); g.Add(input, 1, 0);
            if (list.Children.Count > 0) list.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
            list.Children.Add(g);
        }
        View Money(string value, Action<string> set)
        {
            var e = Box(value, v => { set(v); Check(); }, Keyboard.Numeric);
            e.WidthRequest = 112; e.HorizontalTextAlignment = TextAlignment.End;
            var peso = ClinicTheme.Text("₱", "MutedBody"); peso.VerticalOptions = LayoutOptions.Center;
            return new HorizontalStackLayout { Spacing = 4, Children = { peso, e } };
        }
        Line(ClinicTheme.Text("Standard consultation", "BodyMd"), Money(_vm.FeeConsultation, v => _vm.FeeConsultation = v));
        Line(ClinicTheme.Text("Follow-up consultation", "BodyMd"), Money(_vm.FeeFollowUp, v => _vm.FeeFollowUp = v));
        Line(ClinicTheme.Text("Senior citizen / PWD", "BodyMd"), Money(_vm.FeeSeniorPwd, v => _vm.FeeSeniorPwd = v));
        Line(ClinicTheme.Text("Medical certificate (add-on)", "BodyMd"), Money(_vm.FeeMedCert, v => _vm.FeeMedCert = v));
        var pct = Box(_vm.DiscountPct, v => _vm.DiscountPct = v, Keyboard.Numeric);
        pct.WidthRequest = 112; pct.HorizontalTextAlignment = TextAlignment.End;
        var pctSign = ClinicTheme.Text("%", "MutedBody"); pctSign.VerticalOptions = LayoutOptions.Center;
        Line(new VerticalStackLayout { Children = { ClinicTheme.Text("Senior / PWD discount", "BodyMd"), ClinicTheme.Text("Parked: stored with the fee schedule.", "FieldHint") } },
            new HorizontalStackLayout { Spacing = 4, Children = { pct, pctSign } });
        body.Children.Add(list);
        body.Children.Add(save);
        Check();
    }

    void Payments(VerticalStackLayout body)
    {
        var mode = new Picker { ItemsSource = _vm.PaymentModeOptions.ToList(), SelectedItem = _vm.DefaultPaymentMode };
        mode.SelectedIndexChanged += (_, _) => { if (mode.SelectedItem is string s) _vm.DefaultPaymentMode = s; };
        body.Children.Add(ClinicUi.Field("Default payment mode", ClinicPickers.PickerBox(null, mode, 260)));

        var methods = new Grid { ColumnSpacing = 16 };
        var col = 0;
        foreach (var m in AdminSettingsViewModel.WebPaymentMethods)
        {
            var row = _vm.PaymentMethods.FirstOrDefault(p => p.Method == m);
            if (row is null) continue;
            methods.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var cb = new CheckBox { IsChecked = row.IsAccepted, Color = ClinicTheme.Color("Primary") };
            cb.CheckedChanged += (_, e) => row.IsAccepted = e.Value;
            var label = ClinicTheme.Text(m == PaymentMethod.BankTransfer ? "Bank transfer" : m.ToString(), "BodyMd");
            label.VerticalOptions = LayoutOptions.Center;
            methods.Add(new HorizontalStackLayout { Spacing = 4, Children = { cb, label } }, col++, 0);
        }
        body.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text("Accepted at the front desk", "FieldLabel"), methods } });
        //JJQR - AI (Nov 24, 2026) - owner: Admin switch, under "Accepted at the front desk", above "Refund policy". Saves on its own right away (no Save button); a failure puts it back.
        var autoPaid = new CheckBox { IsChecked = _vm.AutoPaidOnComplete, Color = ClinicTheme.Color("Primary") };
        var reverting = false;
        autoPaid.CheckedChanged += async (_, e) =>
        {
            if (reverting) return;
            var error = await _vm.SetAutoPaidOnCompleteAsync(e.Value);
            if (error is null) { ClinicToast.Show($"Settings saved at {DateTime.Now:h:mm:ss tt}.", ToastKind.Success); return; }
            ClinicToast.Show(error, ToastKind.Error);
            reverting = true; autoPaid.IsChecked = _vm.AutoPaidOnComplete; reverting = false;
        };
        body.Children.Add(ClinicPickers.CheckRow(autoPaid, "Mark the visit paid when the doctor completes it",
            "Records cash for the exact fee, so the front desk does not press Record payment. Waived and ₱0 visits are skipped. No receipt number is given. Saves right away."));
        //JJQR - END
        body.Children.Add(ClinicUi.Field("Refund policy", Area(_vm.RefundPolicy, v => _vm.RefundPolicy = v, 3)));
        body.Children.Add(SaveBar(() => _vm.SaveWebAsync("Payment settings could not be saved. Try again.")));
    }

    void Privacy(VerticalStackLayout body)
    {
        body.Spacing = 24;
        body.Children.Add(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                ClinicUi.Field("Privacy policy", Area(_vm.PrivacyPolicyText, v => _vm.PrivacyPolicyText = v, 6), "Patients read this on the Privacy & consent page."),
                SaveBar(() => _vm.SaveWebAsync("The privacy policy could not be saved. Try again."), "Save policy"),
            },
        });

        // Consent version
        var consent = new VerticalStackLayout { Spacing = 16 };
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(ClinicTheme.Text("Consent version", "HeadlineSm"), 0, 0);
        head.Add(ClinicTheme.Text($"v{_vm.ConsentVersion}", "HeadlineMd"), 1, 0);
        consent.Children.Add(head);
        consent.Children.Add(ClinicTheme.Text("Publish a new version after a material policy change so every patient is asked to consent again.", "FieldHint"));
        if (_bumpConfirm) consent.Children.Add(Warning("This will re-trigger the consent banner for every patient."));
        var publish = ClinicUi.Button(_bumpConfirm ? $"Yes, publish v{_vm.ConsentVersion + 1}" : "Publish new consent version", async () =>
        {
            if (!_bumpConfirm) { _bumpConfirm = true; Render(); return; }
            //JJQR - AI (21 Nov 2026) - online-only
            await _vm.RefreshOnlineAsync();
            if (!_vm.CanSaveOnline) { _vm.SaveError = ClinicNode.Core.Admin.OnlineOnlyAdminSettingsService.NeedsInternet; Render(); return; }
            //JJQR - END
            _saving = true; _vm.SaveError = ""; Render();
            var error = await _vm.PublishConsentWebAsync();
            _saving = false;
            if (error is null) _bumpConfirm = false; else _vm.SaveError = error;
            Render();
        }, _bumpConfirm ? ButtonKind.Primary : ButtonKind.Secondary);
        publish.IsEnabled = !_saving && _vm.CanSaveOnline; //JJQR - AI (21 Nov 2026) online-only //JJQR - END
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { publish } };
        if (_bumpConfirm)
        {
            var cancel = ClinicUi.Button("Cancel", () => { _bumpConfirm = false; Render(); return Task.CompletedTask; }, ButtonKind.Ghost);
            cancel.IsEnabled = !_saving;
            buttons.Children.Add(cancel);
        }
        consent.Children.Add(buttons);
        body.Children.Add(Section(consent));

        body.Children.Add(Section(RequestsPanel()));
    }

    static View Section(View content) => new Border
    {
        Stroke = ClinicTheme.Color("OutlineVariant"),
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        Padding = 16,
        Content = content,
    };

    View RequestsPanel()
    {
        var panel = new VerticalStackLayout { Spacing = 16 };
        panel.Children.Add(ClinicTheme.Text("Patient data requests", "HeadlineSm"));
        panel.Children.Add(ClinicTheme.Text("Correction and deletion requests from patients. Medical records are kept 7 years after the last visit, so a deletion stays Scheduled until then.", "LabelMd"));
        if (!string.IsNullOrWhiteSpace(_vm.RequestError)) panel.Children.Add(ClinicUi.Alert(_vm.RequestError, success: false));
        if (_vm.RequestsFailed) { panel.Children.Add(ClinicUi.Alert("The requests could not be loaded.", success: false)); return panel; }
        if (_vm.RawRequests.Count == 0) { panel.Children.Add(ClinicTheme.Text("No requests yet.", "MutedBody")); return panel; }

        var list = new VerticalStackLayout();
        foreach (var r in _vm.RawRequests)
        {
            var item = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(0, 8) };
            item.Children.Add(new Label
            {
                Style = ClinicTheme.Style("BodyMd"),
                FormattedText = new FormattedString
                {
                    Spans =
                    {
                        new Span { Text = r.PatientName, FontAttributes = FontAttributes.Bold },
                        new Span { Text = " " },
                        new Span { Text = r.PatientCode, FontFamily = "Courier New", TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                        new Span { Text = $" · {r.Kind} · {r.Status}" },
                    },
                },
            });
            item.Children.Add(ClinicTheme.Text(
                $"Requested {AdminViewModelBase.ClinicDate(DateOnly.FromDateTime(r.RequestedAt.ToOffset(ClinicApp.Domain.ClinicClock.Offset).DateTime))}"
                + (r.RetainUntil is { } until ? $" · cannot be deleted before {AdminViewModelBase.ClinicDate(until)}" : ""), "LabelMd"));
            item.Children.Add(ClinicTheme.Text(r.Details, "BodyMd"));
            if (!string.IsNullOrWhiteSpace(r.ResolutionNote)) item.Children.Add(ClinicTheme.Text($"Note: {r.ResolutionNote}", "LabelMd"));
            if (r.Status is "Pending" or "Scheduled")
            {
                var actions = new HorizontalStackLayout { Spacing = 8 };
                if (r.Status == "Pending" && r.Kind == "Deletion") actions.Children.Add(Act(r, "Scheduled", "Schedule", ButtonKind.Secondary));
                actions.Children.Add(Act(r, "Completed", "Mark done", ButtonKind.Secondary));
                actions.Children.Add(Act(r, "Rejected", "Reject…", ButtonKind.Ghost));
                item.Children.Add(actions);
            }
            if (list.Children.Count > 0) list.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
            list.Children.Add(item);
        }
        panel.Children.Add(list);
        return panel;
    }

    Button Act(DataRequestItem r, string status, string label, ButtonKind kind)
    {
        var key = r.Id + status;
        //JJQR - AI (21 Nov 2026) - web Button loading: the label stays, only the busy button is disabled
        var b = ClinicUi.Button(label, async () =>
        {
            string? note = null;
            if (status == "Rejected")
            {
                note = await DisplayPromptAsync("Reject request", "Tell the patient why (shown to them):", "Reject", "Cancel");
                if (string.IsNullOrWhiteSpace(note)) return;
            }
            _busyRequest = key; Render();
            await _vm.ActOnRequestWebAsync(r, status, note);
            _busyRequest = ""; Render();
        }, kind);
        b.IsEnabled = _busyRequest != key;
        //JJQR - END
        return b;
    }

    void Branding(VerticalStackLayout body)
    {
        body.Children.Add(new Border
        {
            Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(12, 8),
            Content = ClinicTheme.Text("Logo/Favicon uploads are preview-only for now — real storage + persistence lands in a later phase.", "PillLabelInfo"),
        });
        View ImagePick(string label, string choose, Func<string?> get, Action<string> set, double size)
        {
            var row = new HorizontalStackLayout { Spacing = 16 };
            row.Children.Add(ClinicUi.Button(choose, async () =>
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images });
                if (file is null) return;
                set(file.FullPath); // local preview only, never saved (same as web)
                Render();
            }, ButtonKind.Secondary));
            if (get() is { } path) row.Children.Add(new Image { Source = ImageSource.FromFile(path), WidthRequest = size, HeightRequest = size, Aspect = Aspect.AspectFill });
            return new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(label, "FieldLabel"), row } };
        }
        body.Children.Add(TwoCols(
            ImagePick("Logo", "Choose logo", () => _logoPreview, v => _logoPreview = v, 48),
            ImagePick("Favicon", "Choose favicon", () => _faviconPreview, v => _faviconPreview = v, 32)));
        View Colour(string label, string value, Action<string> set)
        {
            var swatch = new BoxView { WidthRequest = 44, HeightRequest = 44, CornerRadius = 6, Color = TryColor(value) };
            var entry = Box(value, v => { set(v); swatch.Color = TryColor(v); }, placeholder: "#00685F");
            entry.FontFamily = "Courier New";
            var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            g.Add(swatch, 0, 0); g.Add(entry, 1, 0);
            return ClinicUi.Field(label, g);
        }
        body.Children.Add(TwoCols(
            Colour("Primary colour", _vm.PrimaryColor, v => _vm.PrimaryColor = v),
            Colour("Secondary colour", _vm.SecondaryColor, v => _vm.SecondaryColor = v)));
        body.Children.Add(ClinicUi.Field("Website URL", Box(_vm.WebsiteUrl, v => _vm.WebsiteUrl = v, Keyboard.Url)));
        body.Children.Add(SaveBar(() => _vm.SaveWebAsync("Branding could not be saved. Try again.")));
    }

    static Color TryColor(string? hex) => Color.TryParse(hex ?? "", out var c) ? c : Colors.Black;
}
//JJQR - END
