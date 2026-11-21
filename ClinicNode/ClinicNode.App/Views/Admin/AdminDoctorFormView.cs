//JJQR - AI (21 Nov 2026) - 1:1 with web components/admin/DoctorForm.tsx (used by AdminDoctorNewPage and AdminDoctorEditPage):
// PageHeader, FormAlert, sections Account (Full name, Email [locked on edit + hint], Status on edit) · Practice (Specialty, Consultation fee (₱),
// Slot duration (minutes), warning, Bio (shown to patients)) · Credentials (PRC license number, PTR number, S2 number) · Weekly hours
// (checkbox + day, start – end or "Not working"), footer Cancel / Send invite | Save changes.
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public static class AdminDoctorFormView
{
    public static View Build(IAdminDoctorForm vm)
    {
        var root = new VerticalStackLayout { Padding = 24, Spacing = 24, MaximumWidthRequest = 960 };
        root.BindingContext = vm;

        var title = ClinicTheme.Text(vm.FormTitle, "HeadlineMd");
        title.SetBinding(Label.TextProperty, nameof(IAdminDoctorForm.FormTitle));
        root.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { title, ClinicTheme.Text(vm.FormDescription, "MutedBody") } });

        var formAlert = new VerticalStackLayout();
        root.Children.Add(formAlert);

        Entry Bound(string path, Keyboard? keyboard = null, string? font = null)
        {
            var e = new Entry { Keyboard = keyboard ?? Keyboard.Default };
            if (font is not null) e.FontFamily = font;
            e.SetBinding(Entry.TextProperty, path, BindingMode.TwoWay);
            return e;
        }

        var save = new Button { Text = vm.IsEdit ? "Save changes" : "Send invite", Style = ClinicTheme.Style("PrimaryButton") };
        var warning = new VerticalStackLayout();

        void Refresh()
        {
            save.IsEnabled = DoctorFormRules.CanSave(vm);
            warning.Children.Clear();
            if (DoctorFormRules.Problem(vm.ConsultationFee, vm.SlotDurationMinutes) is { } p)
                warning.Children.Add(new Border { Style = ClinicTheme.Style("PillWarning"), Padding = new Thickness(12, 8), Content = ClinicTheme.Text(p, "PillLabelWarning") });
            formAlert.Children.Clear();
            if (!string.IsNullOrWhiteSpace(vm.FormError)) formAlert.Children.Add(ClinicUi.Alert(vm.FormError, success: false));
        }
        void Watch(Entry e) => e.TextChanged += (_, _) => e.Dispatcher.Dispatch(Refresh);

        static Grid Cols(int n)
        {
            var g = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
            for (var i = 0; i < n; i++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            return g;
        }
        static Border Section(params View[] children)
        {
            var stack = new VerticalStackLayout { Spacing = 16 };
            foreach (var c in children) stack.Children.Add(c);
            return new Border { Style = ClinicTheme.Style("Card"), Content = stack };
        }

        // Account
        var name = Bound(nameof(IAdminDoctorForm.FullName)); Watch(name);
        var email = Bound(nameof(IAdminDoctorForm.Email), Keyboard.Email); Watch(email);
        email.IsEnabled = !vm.IsEdit;
        var account = Cols(2);
        account.Add(ClinicUi.Field("Full name", name), 0, 0);
        account.Add(ClinicUi.Field("Email", email, hint: vm.IsEdit ? "The sign-in email can't be changed here." : null), 1, 0);
        if (vm is AdminDoctorEditViewModel edit)
        {
            account.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            account.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var status = new Picker { ItemsSource = edit.StatusOptions.ToList() };
            status.SetBinding(Picker.SelectedItemProperty, nameof(AdminDoctorEditViewModel.StatusChoice), BindingMode.TwoWay);
            account.Add(ClinicUi.Field("Status", status), 0, 1);
        }
        root.Children.Add(Section(ClinicTheme.Text("Account", "HeadlineSm"), account));

        // Practice
        var spec = Bound(nameof(IAdminDoctorForm.Specialization)); Watch(spec);
        var fee = Bound(nameof(IAdminDoctorForm.ConsultationFee), Keyboard.Numeric); Watch(fee);
        var slot = Bound(nameof(IAdminDoctorForm.SlotDurationMinutes), Keyboard.Numeric); Watch(slot);
        var practice = Cols(2);
        practice.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        practice.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        practice.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        practice.Add(ClinicUi.Field("Specialty", spec), 0, 0);
        practice.Add(ClinicUi.Field("Consultation fee (₱)", fee), 1, 0);
        practice.Add(ClinicUi.Field("Slot duration (minutes)", slot), 0, 1);
        practice.Add(warning, 0, 2);
        Grid.SetColumnSpan(warning, 2);
        var bio = new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 88 };
        bio.SetBinding(Editor.TextProperty, "Bio", BindingMode.TwoWay);
        var bioLabel = new Label
        {
            Style = ClinicTheme.Style("FieldLabel"),
            FormattedText = new FormattedString
            {
                Spans = { new Span { Text = "Bio " }, new Span { Text = "(shown to patients)", TextColor = ClinicTheme.Color("OnSurfaceVariant") } },
            },
        };
        root.Children.Add(Section(ClinicTheme.Text("Practice", "HeadlineSm"), practice,
            new VerticalStackLayout { Spacing = 4, Children = { bioLabel, bio } }));

        // Credentials
        var creds = Cols(3);
        creds.Add(ClinicUi.Field("PRC license number", Bound("LicenseNumber", font: "Courier New")), 0, 0);
        creds.Add(ClinicUi.Field("PTR number", Bound("PtrNumber", font: "Courier New")), 1, 0);
        creds.Add(ClinicUi.Field("S2 number", Bound("S2Number", font: "Courier New")), 2, 0);
        root.Children.Add(Section(
            new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text("Credentials", "HeadlineSm"), ClinicTheme.Text("Printed on prescriptions and certificates.", "FieldHint") } },
            creds));

        // Weekly hours
        var hours = new VerticalStackLayout { Spacing = 0 };
        void BuildHours()
        {
            hours.Children.Clear();
            var first = true;
            foreach (var day in vm.Schedule)
            {
                if (!first) hours.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
                first = false;
                hours.Children.Add(DayRow(day));
            }
        }
        BuildHours();
        // the edit form fills the week after loading
        vm.Schedule.CollectionChanged += (_, _) => hours.Dispatcher.Dispatch(BuildHours);
        root.Children.Add(Section(ClinicTheme.Text("Weekly hours", "HeadlineSm"), hours));

        // Footer
        var cancel = new Button { Text = "Cancel", Style = ClinicTheme.Style("SecondaryButton") };
        cancel.Clicked += async (_, _) => await vm.CancelWebAsync();
        save.Clicked += async (_, _) =>
        {
            if (!save.IsEnabled) return;
            save.IsEnabled = false;
            save.Text = vm.IsEdit ? "Saving…" : "Sending invite…";
            try { await vm.SaveWebAsync(); }
            finally
            {
                save.Text = vm.IsEdit ? "Save changes" : "Send invite";
                Refresh();
            }
        };
        root.Children.Add(new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { cancel, save } });

        if (vm is System.ComponentModel.INotifyPropertyChanged npc)
            npc.PropertyChanged += (_, _) => root.Dispatcher.Dispatch(Refresh);
        Refresh();
        return new ScrollView { Content = root };
    }

    static View DayRow(ScheduleRow day)
    {
        var check = new CheckBox { IsChecked = day.IsActive, VerticalOptions = LayoutOptions.Center, Color = ClinicTheme.Color("Primary") };
        var label = ClinicTheme.Text(day.DayName, "BodyMd");
        label.VerticalOptions = LayoutOptions.Center;
        var start = new TimePicker { Time = day.Start, WidthRequest = 136 };
        var end = new TimePicker { Time = day.End, WidthRequest = 136 };
        start.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimePicker.Time) && start.Time is { } t) day.Start = t; };
        end.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimePicker.Time) && end.Time is { } t) day.End = t; };
        var times = new HorizontalStackLayout { Spacing = 8, Children = { start, new Label { Text = "–", VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") }, end } };
        var off = ClinicTheme.Text("Not working", "MutedBody");
        off.VerticalOptions = LayoutOptions.Center;
        void Apply() { times.IsVisible = day.IsActive; off.IsVisible = !day.IsActive; }
        check.CheckedChanged += (_, e) => { day.IsActive = e.Value; Apply(); };
        Apply();
        var grid = new Grid
        {
            Padding = new Thickness(0, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        grid.Add(new HorizontalStackLayout { Spacing = 8, MinimumWidthRequest = 128, Children = { check, label } }, 0, 0);
        var right = new Grid { HorizontalOptions = LayoutOptions.End };
        right.Children.Add(times);
        right.Children.Add(off);
        grid.Add(right, 1, 0);
        return grid;
    }
}
//JJQR - END
