//JJQR - AI (2026-11-26) - GAV-11: owner: "can the doctor ... tag this patient as DIFFERENT and create a new one and carry over the consultation".
// Opened from "Wrong patient?" right after "Edit details" in the consultation header (web WrongPatientDialog). Online only, syncs first:
//   1. this computer's unsent work for the visit is synced (IVisitSyncGate); if any is still waiting, nothing opens (it would land on the wrong person)
//   2. GET reassign-preview: what moves (payment/OR too, also when finished or paid), and the allergies (added during this visit = ticked by default)
//   3. New patient (details) or Existing patient (search); the chosen person's same-day visit is shown as a warning right away
//   4. reason, Confirm: the server moves it in one transaction; "same day" and "same name + birthday" come back as questions → "Move anyway"
// Reuses AdminDialog.ShowSubmitAsync (Confirm runs while it stays open; a refusal shows inside it; it closes first, then answers).
// Windows rules: every control is created new each time the dialog opens and the search results are new controls each search (never reused with an
// old parent); the caller redraws on the next UI turn after the pop-up has closed. Nothing is logged (patient data).
using System.Globalization;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Doctor.Extra;

public static class WrongPatientDialog
{
    /// <summary>Shows the dialog; returns what moved (dialog already closed), or null when cancelled / refused before opening.</summary>
    public static async Task<WrongPatientMoved?> ShowAsync(Page page, IServiceProvider sp, Guid bookingId, Guid currentPatientId)
    {
        var svc = sp.GetRequiredService<IVisitReassignService>();
        var gate = sp.GetService<IVisitSyncGate>();
        var patients = sp.GetRequiredService<IAdminPatientService>();
        WrongPatientPreview preview;
        try
        {
            // the preview first: offline it answers "needs an internet connection" without running a sync that cannot work
            var opening = await svc.GetPreviewAsync(bookingId);
            if (!opening.Succeeded || opening.Value is null) { ClinicToast.Show(opening.Error ?? "The visit could not be loaded. Nothing was changed.", ToastKind.Error); return null; }
            if (gate is not null && await gate.SyncFirstAsync(bookingId) is { } waiting) { ClinicToast.Show(waiting, ToastKind.Error); return null; }
            var fresh = await svc.GetPreviewAsync(bookingId); // after the sync (the counts / allergies now include what was just sent)
            preview = fresh.Succeeded && fresh.Value is not null ? fresh.Value : opening.Value;
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("WrongPatientDialog.Load", ex);
            ClinicToast.Show("The visit could not be loaded. Nothing was changed; try again.", ToastKind.Error);
            return null;
        }

        var today = ClinicApp.Domain.ClinicClock.Today;
        var newId = Guid.NewGuid(); // one id per dialog: a retried Confirm never creates a second patient
        var group = "wp-" + Guid.NewGuid().ToString("N"); // a new radio group every time
        var ackSameDay = false;
        var ackDuplicate = false;
        AdminDialog? dlg = null;
        void ResetQuestions()
        {
            ackSameDay = false; ackDuplicate = false;
            if (dlg is not null) dlg.ConfirmButton.Text = "Confirm move";
        }

        // ── what this visit is and what moves ──
        var who = preview.Patient;
        var visitLine = $"{preview.AppointmentDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(preview.QueueNumber) ? "" : " · " + preview.QueueNumber)} · {preview.ConsultationStatus ?? preview.Status}"
                        + (string.IsNullOrWhiteSpace(preview.PaymentStatus) ? "" : $" · {preview.PaymentStatus}{(string.IsNullOrWhiteSpace(preview.OrNumber) ? "" : " (OR " + preview.OrNumber + ")")}");
        var summary = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = $"If this is a different person, move this visit to the right patient. {who.Name}'s details and other visits stay as they are.", Style = ClinicTheme.Style("BodyMd") },
                new Label { Text = $"This visit: {visitLine} — now under {who.Name} ({who.PatientCode})", Style = ClinicTheme.Style("MutedSm") },
                new Label { Text = WrongPatientRules.MovesSummary(preview), Style = ClinicTheme.Style("MutedSm"), AutomationId = "wp-summary" },
            },
        };
        var summaryBox = new Border { Content = summary, Padding = new Thickness(12, 10), BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Stroke = ClinicTheme.Color("OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 } };

        // ── New / Existing ──
        var rbNew = new RadioButton { Content = "New patient", GroupName = group, IsChecked = true };
        var rbExisting = new RadioButton { Content = "Existing patient", GroupName = group };
        AutomationProperties.SetName(rbNew, "New patient");
        AutomationProperties.SetName(rbExisting, "Existing patient");
        var choice = new HorizontalStackLayout { Spacing = 24, Children = { rbNew, rbExisting } };

        // new patient
        Entry Box(Keyboard? kb = null, int max = 100) => new() { Keyboard = kb ?? Keyboard.Default, MaxLength = max };
        var first = Box(); var middle = Box(); var last = Box();
        var sex = new Picker { ItemsSource = new List<string> { "Male", "Female" }, SelectedItem = "Male" };
        var dob = new DatePicker { Date = null, MaximumDate = today.ToDateTime(TimeOnly.MinValue), MinimumDate = new DateTime(1900, 1, 1) };
        var age = new Label { Style = ClinicTheme.Style("BodyMd"), VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 10, 0, 0), Text = "—" };
        dob.DateSelected += (_, _) => { age.Text = dob.Date is { } d ? $"{PatientDetailsRules.AgeOn(DateOnly.FromDateTime(d), today)} years" : "—"; ResetQuestions(); };
        var contact = Box(Keyboard.Telephone, 20);
        var address = Box(max: 300);
        foreach (var e in new[] { first, middle, last }) e.TextChanged += (_, _) => ResetQuestions();
        var newGrid = new Grid { ColumnSpacing = 16, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        var r = 0;
        void Pair(View a, View? b = null) { newGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); newGrid.Add(a, 0, r); if (b is not null) newGrid.Add(b, 1, r); r++; }
        Pair(ClinicUi.Field("First name", first, required: true), ClinicUi.Field("Middle name", middle));
        Pair(ClinicUi.Field("Last name", last, required: true), ClinicUi.Field("Sex", ClinicPickers.PickerBox(null, sex)));
        Pair(ClinicUi.Field("Birthday", ClinicPickers.DateBox(null, dob, new DateTime(1990, 1, 1), true), required: true), ClinicUi.Field("Age", age));
        Pair(ClinicUi.Field("Contact number", contact), ClinicUi.Field("Address", address));
        var newPanel = new VerticalStackLayout { Spacing = 6, Children = { newGrid, new Label { Text = "Other details can be added later with Edit details.", Style = ClinicTheme.Style("MutedSm") } } };

        // existing patient (search)
        WrongPatientTarget? target = null;
        var search = new Entry { Placeholder = "Name, patient code or phone", MaxLength = 100 };
        AutomationProperties.SetName(search, "Search patients");
        var searchButton = new Button { Text = "Search", Style = ClinicTheme.Style("SecondaryButton") };
        var results = new VerticalStackLayout { Spacing = 4 };
        var chosen = new Label { Style = ClinicTheme.Style("LabelMd"), IsVisible = false };
        var sameDay = new Label { Style = ClinicTheme.Style("FormAlertErrorText") };
        var sameDayBox = new Border { Style = ClinicTheme.Style("FormAlertError"), Content = sameDay, IsVisible = false };
        var searchSeq = 0;
        async Task RunSearchAsync()
        {
            var q = (search.Text ?? "").Trim();
            var mine = ++searchSeq;
            results.Children.Clear();
            if (q.Length < 2) { results.Children.Add(new Label { Text = "Type at least 2 letters.", Style = ClinicTheme.Style("MutedSm") }); return; }
            results.Children.Add(new Label { Text = "Searching…", Style = ClinicTheme.Style("MutedSm") });
            AdminPage<AdminPatientListItem> found;
            try { found = await patients.SearchAsync(q, 1, 8); }
            catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("WrongPatientDialog.Search", ex); found = new AdminPage<AdminPatientListItem>([], 0, 1, 8); }
            if (mine != searchSeq) return; // an older search answered late
            UiTurn.Next(() =>
            {
                results.Children.Clear(); // new controls every search
                var list = found.Items.Where(p => p.PatientId != currentPatientId).ToList();
                if (list.Count == 0) { results.Children.Add(new Label { Text = "No other patient matches.", Style = ClinicTheme.Style("MutedSm") }); return; }
                foreach (var p in list)
                {
                    var b = new Button { Text = $"{p.FirstName} {p.LastName} · {p.PatientCode} · born {p.DateOfBirth.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}", Style = ClinicTheme.Style("SecondaryButton"), HorizontalOptions = LayoutOptions.Fill };
                    var pid = p.PatientId;
                    b.Clicked += async (_, _) => await ChooseAsync(pid);
                    results.Children.Add(b);
                }
            });
        }
        async Task ChooseAsync(Guid pid)
        {
            ResetQuestions();
            var got = await svc.GetPreviewAsync(bookingId, pid);
            UiTurn.Next(() =>
            {
                target = got.Succeeded ? got.Value?.Target : null;
                if (target is null) { chosen.Text = got.Error ?? "That patient could not be loaded."; chosen.IsVisible = true; sameDayBox.IsVisible = false; return; }
                if (target.Unavailable) { chosen.Text = "That patient was merged or removed, or is the one already on this visit. Choose another."; chosen.IsVisible = true; sameDayBox.IsVisible = false; target = null; return; }
                chosen.Text = $"Move to: {target.Person.Name} ({target.Person.PatientCode})";
                chosen.IsVisible = true;
                sameDayBox.IsVisible = target.HasVisitThatDay;
                if (target.HasVisitThatDay) { sameDay.Text = WrongPatientRules.SameDayText(target, preview.AppointmentDate); ackSameDay = true; } // shown before Confirm = asked
            });
        }
        searchButton.Clicked += async (_, _) => await searchButton.RunBusyAsync(RunSearchAsync);
        search.Completed += async (_, _) => await RunSearchAsync();
        var searchRow = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        searchRow.Add(search, 0, 0); searchRow.Add(searchButton, 1, 0);
        var existingPanel = new VerticalStackLayout { Spacing = 8, IsVisible = false, Children = { ClinicUi.Field("Find the right patient", searchRow), results, chosen, sameDayBox } };

        rbNew.CheckedChanged += (_, e) => { if (!e.Value) return; ResetQuestions(); UiTurn.Next(() => { newPanel.IsVisible = true; existingPanel.IsVisible = false; }); };
        rbExisting.CheckedChanged += (_, e) => { if (!e.Value) return; ResetQuestions(); UiTurn.Next(() => { newPanel.IsVisible = false; existingPanel.IsVisible = true; if (target?.HasVisitThatDay == true) ackSameDay = true; }); };

        // ── allergies ──
        var ticks = new List<(CheckBox Box, Guid Id)>();
        var allergyPanel = new VerticalStackLayout { Spacing = 4 };
        if (preview.Allergies.Count > 0)
        {
            allergyPanel.Children.Add(new Label { Text = "ALLERGIES TO MOVE WITH THIS VISIT", Style = ClinicTheme.Style("TableHeader") });
            var defaults = WrongPatientRules.DefaultAllergyIds(preview).ToHashSet();
            foreach (var a in preview.Allergies)
            {
                var box = new CheckBox { IsChecked = defaults.Contains(a.Id), Color = ClinicTheme.Color("Primary") };
                AutomationProperties.SetName(box, a.Label);
                ticks.Add((box, a.Id));
                allergyPanel.Children.Add(new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        box,
                        new Label { Text = a.Label, Style = ClinicTheme.Style("BodyMd"), VerticalOptions = LayoutOptions.Center },
                        new Label { Text = a.AddedDuringVisit ? "added during this visit" : "on file before", Style = ClinicTheme.Style("MutedSm"), VerticalOptions = LayoutOptions.Center },
                    },
                });
            }
            allergyPanel.Children.Add(new Label { Text = $"Ticked allergies move to the right patient; unticked ones stay with {who.Name}.", Style = ClinicTheme.Style("MutedSm") });
        }

        // ── reason ──
        var reason = new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 70, MaxLength = WrongPatientRules.ReasonMax, Placeholder = "e.g. Different person with the same name" };
        AutomationProperties.SetName(reason, "Why? (kept in the audit log)");

        var form = new VerticalStackLayout
        {
            Spacing = 12,
            Children = { summaryBox, ClinicUi.Field("Who is this person?", choice), newPanel, existingPanel, allergyPanel, ClinicUi.Field("Why? (kept in the audit log)", reason, required: true) },
        };
        var body = new ScrollView { Content = form, MaximumHeightRequest = 600 };

        WrongPatientMoved? moved = null;
        var ok = await AdminDialog.ShowSubmitAsync(page, "Wrong patient?", body, "Confirm move", async () =>
        {
            var isNew = rbNew.IsChecked;
            var request = new WrongPatientRequest(
                isNew,
                isNew ? new WrongPatientNewPerson(newId, first.Text ?? "", middle.Text, last.Text ?? "", sex.SelectedItem as string == "Female" ? SexType.Female : SexType.Male,
                    dob.Date is { } d ? DateOnly.FromDateTime(d) : null, contact.Text, address.Text) : null,
                isNew ? null : target?.Person.PatientId,
                reason.Text ?? "",
                ticks.Where(t => t.Box.IsChecked).Select(t => t.Id).ToList(),
                AcknowledgeSameDayVisit: ackSameDay, AcknowledgePossibleDuplicate: ackDuplicate);
            if (WrongPatientRules.Problem(request, currentPatientId, today) is { } bad) return bad;
            if (gate is not null && await gate.PendingProblemAsync(bookingId) is { } waiting) return waiting; // typed elsewhere meanwhile
            var outcome = await svc.ReassignAsync(bookingId, request);
            if (outcome.Succeeded) { moved = outcome.Moved; return null; }
            string? relabel = null;
            string text = outcome.Error ?? "The visit could not be moved, so nothing was changed. Try again.";
            if (outcome.Warning == WrongPatientWarning.SameDayVisit) { ackSameDay = true; relabel = "Move anyway"; text += " Press \"Move anyway\" to continue, or Cancel."; }
            else if (outcome.Warning == WrongPatientWarning.PossibleDuplicate)
            {
                ackDuplicate = true; relabel = "Create new patient anyway";
                var m = outcome.Matches ?? [];
                if (m.Count > 0) text += " On file: " + string.Join("; ", m.Select(x => $"{x.Name} ({x.PatientCode})")) + ". To use that record, choose Existing patient and search for it.";
            }
            // AdminDialog puts the old label back after this returns: relabel on the next turn
            if (relabel is not null && dlg is not null) { var dd = dlg; dd.Dispatcher.Dispatch(() => dd.ConfirmButton.Text = relabel); }
            return text;
        }, busyText: "Moving…", onOpen: d => dlg = d);
        return ok ? moved : null;
    }

    /// <summary>The "Wrong patient?" button text/icon (web: person_off). A new Button every call.</summary>
    public static void Decorate(Button b)
    {
        b.ImageSource = ClinicIcons.FontImage("person_off", 14, ClinicTheme.Color("Primary"));
        AutomationProperties.SetName(b, "Wrong patient?");
    }

    public static string MovedToast(WrongPatientMoved m) =>
        m.Already ? "This visit was already under that patient." : $"Visit moved to {m.PatientCode} at {DateTime.Now.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)}.";
}
//JJQR - END
