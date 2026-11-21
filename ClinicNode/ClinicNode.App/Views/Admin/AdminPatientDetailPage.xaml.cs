//JJQR - AI (21 Nov 2026) - web /admin/patients/[id] layout (see AdminPatientDetailPage.xaml)
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public partial class AdminPatientDetailPage : ContentPage
{
    readonly AdminPatientDetailViewModel _vm;
    string _tab = "overview";

    public AdminPatientDetailPage(AdminPatientDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AdminPatientDetailViewModel.Detail) or nameof(AdminPatientDetailViewModel.IsBusy))
                MainThread.BeginInvokeOnMainThread(Render);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    void Render()
    {
        Root.Children.Clear();
        var back = new Button { Text = "‹ All patients", Style = ClinicTheme.Style("GhostButton"), HorizontalOptions = LayoutOptions.Start };
        back.Clicked += async (_, _) => { try { await Shell.Current.GoToAsync("//admin-patients"); } catch { } };

        var d = _vm.Detail;
        if (d is null)
        {
            Root.Children.Add(back);
            if (_vm.HasError)
            {
                Root.Children.Add(ClinicTheme.Text("Patient unavailable", "HeadlineSm"));
                Root.Children.Add(ClinicTheme.Text("This patient's record could not be loaded. Nothing has changed; try again.", "MutedBody"));
                Root.Children.Add(ClinicUi.Button("Try again", () => _vm.LoadAsync(), ButtonKind.Secondary));
            }
            else Root.Children.Add(new ActivityIndicator { IsRunning = true });
            return;
        }
        var p = d.Patient;
        Root.Children.Add(back);

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        var sub = new Label
        {
            Style = ClinicTheme.Style("MutedBody"),
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = p.PatientCode, FontFamily = "Courier New", FontSize = 13 },
                    new Span { Text = $" · {p.Sex} · Born {AdminViewModelBase.ClinicDate(p.DateOfBirth)}" },
                },
            },
        };
        header.Add(new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text($"{p.FirstName} {p.LastName}".Trim(), "HeadlineLg"), sub } }, 0, 0);
        var edit = ClinicUi.Button("Edit details", EditAsync, ButtonKind.Secondary);
        edit.VerticalOptions = LayoutOptions.Start;
        header.Add(edit, 1, 0);
        Root.Children.Add(header);

        // Tabs
        var tabs = new HorizontalStackLayout { Spacing = 4 };
        foreach (var (id, label) in new[] { ("overview", "Overview"), ("bookings", $"Bookings ({d.Bookings.Count})"), ("records", $"Consultations ({d.Consultations.Count})"), ("access", "Access log") })
        {
            var b = new Button { Text = label, Style = ClinicTheme.Style(_tab == id ? "SegmentedButtonSelected" : "SegmentedButton") };
            var captured = id;
            b.Clicked += (_, _) => { _tab = captured; Render(); };
            tabs.Children.Add(b);
        }
        var content = new VerticalStackLayout { Spacing = 0 };
        switch (_tab)
        {
            case "overview":
                var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
                grid.Add(Info("Sex", p.Sex.ToString()), 0, 0);
                grid.Add(Info("Date of birth", AdminViewModelBase.ClinicDate(p.DateOfBirth)), 1, 0);
                grid.Add(Info("Contact number", p.ContactNumber), 0, 1);
                grid.Add(Info("Email", p.Email, wrapAll: true), 1, 1);
                content.Children.Add(grid);
                break;
            case "bookings":
                if (d.Bookings.Count == 0) content.Children.Add(Muted("No bookings for this patient yet."));
                foreach (var b in d.Bookings)
                {
                    var row = new Grid { Padding = new Thickness(0, 10), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                    row.Add(new VerticalStackLayout
                    {
                        Children =
                        {
                            ClinicTheme.Text(AdminViewModelBase.ClinicDate(b.AppointmentDate), "LabelLg"),
                            ClinicTheme.Text(string.IsNullOrWhiteSpace(b.DoctorName) ? "Unknown doctor" : b.DoctorName, "MutedSm"),
                        },
                    }, 0, 0);
                    row.Add(AdminPill.Make(b.Status.ToString()), 1, 0);
                    var id = b.BookingId;
                    row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => { try { await Shell.Current.GoToAsync($"admin-booking-detail?id={id}"); } catch { } }) });
                    AddDivided(content, row);
                }
                break;
            case "records":
                if (d.Consultations.Count == 0) content.Children.Add(Muted("No consultations recorded for this patient."));
                foreach (var c in d.Consultations)
                {
                    AddDivided(content, new VerticalStackLayout
                    {
                        Padding = new Thickness(0, 10),
                        Children =
                        {
                            ClinicTheme.Text(c.Date == default ? "Date not recorded" : AdminViewModelBase.ClinicDateShort(c.Date), "MutedSm"),
                            ClinicTheme.Text(string.IsNullOrWhiteSpace(c.ChiefComplaint) ? "No chief complaint recorded" : c.ChiefComplaint!, "BodyMd"),
                        },
                    });
                }
                break;
            default:
                //JJQR - AI (21 Nov 2026) - web AccessLogPanel failed state
                if (d.AccessLogFailed) { content.Children.Add(Muted("The access log could not be loaded.")); break; }
                //JJQR - END
                if (d.AccessLog.Count == 0) { content.Children.Add(Muted("No one on the clinic team has opened this record yet.")); break; }
                content.Children.Add(new Label { Text = "Every time clinic staff, a doctor or an admin opens this patient's record, it is written here.", Style = ClinicTheme.Style("MutedSm"), Margin = new Thickness(0, 0, 0, 8) });
                foreach (var a in d.AccessLog)
                {
                    var row = new Grid { Padding = new Thickness(0, 10), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                    row.Add(new Label
                    {
                        Style = ClinicTheme.Style("BodyMd"),
                        FormattedText = new FormattedString
                        {
                            Spans =
                            {
                                new Span { Text = a.Who + " " },
                                new Span { Text = $"({(string.IsNullOrWhiteSpace(a.Role) ? "—" : a.Role)})", TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                                new Span { Text = " · " + a.Area },
                            },
                        },
                    }, 0, 0);
                    row.Add(ClinicTheme.Text(AdminViewModelBase.ClinicDateTime(a.At), "MutedSm"), 1, 0);
                    AddDivided(content, row);
                }
                break;
        }
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = new VerticalStackLayout { Spacing = 24, Children = { tabs, content } } });
    }

    static View Info(string label, string? value, bool wrapAll = false) => new VerticalStackLayout
    {
        Spacing = 2,
        Children =
        {
            ClinicTheme.Text(label, "FieldLabel"),
            new Label { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, Style = ClinicTheme.Style("BodyMd"), LineBreakMode = wrapAll ? LineBreakMode.CharacterWrap : LineBreakMode.WordWrap },
        },
    };

    static Label Muted(string text) => new() { Text = text, Style = ClinicTheme.Style("MutedBody"), Margin = new Thickness(0, 12) };

    static void AddDivided(VerticalStackLayout list, View row)
    {
        if (list.Children.Count > 0) list.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
        list.Children.Add(row);
    }

    //JJQR - AI (21 Nov 2026) - web Modal "Edit patient details": Save runs while the modal stays open ("Saving…", Cancel disabled);
    // a failure shows the FormAlert inside the open modal instead of closing and reopening it. Web DatePicker has no max date.
    async Task EditAsync()
    {
        var p = _vm.Detail?.Patient;
        if (p is null) return;
        var first = new Entry { Text = p.FirstName };
        var last = new Entry { Text = p.LastName };
        var dob = new DatePicker { Date = p.DateOfBirth.ToDateTime(TimeOnly.MinValue) };
        var sex = new Picker { ItemsSource = new List<string> { "Male", "Female" }, SelectedItem = p.Sex.ToString() == "Female" ? "Female" : "Male" };
        var contact = new Entry { Text = p.ContactNumber ?? "", Keyboard = Keyboard.Telephone };
        var email = new Entry { Text = p.Email ?? "", Keyboard = Keyboard.Email };
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Add(ClinicUi.Field("First name", first), 0, 0);
        grid.Add(ClinicUi.Field("Last name", last), 1, 0);
        grid.Add(ClinicUi.Field("Date of birth", dob), 0, 1);
        grid.Add(ClinicUi.Field("Sex", sex), 1, 1);
        grid.Add(ClinicUi.Field("Contact number", contact), 0, 2);
        grid.Add(ClinicUi.Field("Email", email), 1, 2);
        var saved = await AdminDialog.ShowSubmitAsync(this, "Edit patient details", grid, "Save changes",
            () => _vm.SaveWebAsync(first.Text ?? "", last.Text ?? "", dob.Date, sex.SelectedItem as string ?? "Male", contact.Text ?? "", email.Text ?? ""),
            busyText: "Saving…");
        if (saved) ClinicToast.Show($"Patient saved at {DateTime.Now:h:mm:ss tt}.", ToastKind.Success);
    }
    //JJQR - END

}
//JJQR - END
