//JJQR - AI (21 Nov 2026) - line-by-line UI pass vs Project02-fe consultation2/[bookingId]/page.tsx + ConsultPatientBanner.tsx + VitalsEditor.tsx:
//   patient banner (name · sex · age, coloured "Allergies:" chip that opens the editor, "Record allergies / NKDA", NKDA confirm),
//   section indicators (✓ / ○ / ⚠ / n-of-m), numbered section titles, footer save-status wording, "Discard changes?" and
//   "Confirm Lab Orders" dialogs, the justCompleted summary, bold labels on the read-only summary cards.
using System.Globalization;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;
using ClinicNode.Core.Patients;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    private bool _allergyEditorOpen;
    private bool _saveFailed;
    private string? _amendSavedAt;
    private Label? _checklistError;
    //JJQR - AI (21 Nov 2026) - ALL-1 banner chips: web booking.paymentStatus + clinicalBannerProps (online-first via IConsultationLibraryService)
    private string? _paymentDisplay;
    private PatientClinicalFlags? _bannerFlags;

    async Task LoadBannerDataAsync()
    {
        if (_workspace is null) return;
        try { _bannerFlags = _clinicalFlags ?? await _lib.GetPatientClinicalFlagsAsync(D.PatientId); } catch { _bannerFlags = null; }
        try { _paymentDisplay = await _lib.GetBookingPaymentDisplayAsync(D.BookingId); } catch { _paymentDisplay = null; }
        RenderBanner();
    }

    // web lib/clinical/ob.ts aogFromLmp / eddFromLmp (dates only)
    static (int Weeks, int Days, string Label) AogFromLmp(DateOnly lmp, DateOnly today)
    {
        var total = Math.Max(0, today.DayNumber - lmp.DayNumber);
        int w = total / 7, d = total % 7;
        return (w, d, $"{w}w {d}d");
    }
    static string EddFromLmp(DateOnly lmp) => lmp.AddDays(280).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static Border Chip(string text, string bg, string fg, string border)
    {
        var l = ClinicTheme.Text(text, "LabelSm");
        l.TextColor = ClinicTheme.Color(fg);
        return new Border
        {
            BackgroundColor = ClinicTheme.Color(bg), Stroke = ClinicTheme.Color(border), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 999 },
            Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 8, 4), VerticalOptions = LayoutOptions.Center, Content = l,
        };
    }
    //JJQR - END

    /// <summary>web: the Complete error shows inside the open Completion Checklist (falls back to the footer).</summary>
    void ChecklistError(string message)
    {
        _saveFailed = true;
        if (Overlay.IsVisible && _checklistError is not null) { _checklistError.Text = message; _checklistError.IsVisible = true; }
        else Error(message);
    }

    void OnHelp(object? sender, EventArgs e) => ShowShortcutHelp();

    // ---------- banner (web ConsultPatientBanner) ----------
    static string FormatAllergen(string? allergen)
    {
        var t = (allergen ?? "").Trim();
        if (t.Length == 0 || t.StartsWith("R4", StringComparison.OrdinalIgnoreCase)) return "Unnamed allergen (review)";
        if (t.Length > 20 && !t.Any(char.IsWhiteSpace) && t.All(char.IsLetterOrDigit)) return "Unnamed allergen (review)";
        return t;
    }

    void RenderBanner()
    {
        var w = _workspace!;
        var nkda = _allergies.Any(a => a.IsNkda);
        var active = _allergies.Where(a => !a.IsNkda).ToList();
        var junk = active.Any(a => FormatAllergen(a.Allergen) == "Unnamed allergen (review)");
        var (text, amber) = nkda ? ("NKDA", false)
            : active.Count == 0 ? ("No allergies recorded", true)
            : (string.Join("; ", active.Select(a => FormatAllergen(a.Allergen) + (string.IsNullOrWhiteSpace(a.Reaction) ? "" : $" ({a.Reaction})"))), junk);
        var strong = !nkda && active.Count > 0 && !amber;
        var needsReview = !nkda && (active.Count == 0 || junk);

        // "{name} · {sex} · {age}y" (patient code when the age is unknown)
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(w.PatientSex)) meta.Add(w.PatientSex!);
        if (w.PatientDateOfBirth is { } dob) meta.Add($"{AgeOn(dob, ClinicClock.Today)}y");
        else if (!string.IsNullOrWhiteSpace(w.PatientCode)) meta.Add(w.PatientCode);
        var nameLine = new FormattedString();
        nameLine.Spans.Add(new Span { Text = string.IsNullOrWhiteSpace(w.PatientName) ? "—" : w.PatientName, FontAttributes = FontAttributes.Bold, TextColor = ClinicTheme.Color("OnSurface") });
        if (meta.Count > 0) nameLine.Spans.Add(new Span { Text = " · " + string.Join(" · ", meta), TextColor = ClinicTheme.Color("OnSurfaceVariant") });
        BannerName.FormattedText = nameLine;
        BannerName.Style = ClinicTheme.Style("BodyMd");
        BannerMeta.IsVisible = false;

        AllergyBar.Children.Clear();
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        //JJQR - AI (21 Nov 2026) - ALL-1: web chips in order: Preg / LMP weeks, G/P, Guardian (minors only), then the allergy chip
        var f = _bannerFlags ?? _clinicalFlags;
        var isPregnant = f?.IsPregnant == true;
        var lmp = f?.LmpDate;
        var showOb = isPregnant || lmp is not null;
        var aog = lmp is { } l0 ? AogFromLmp(l0, ClinicClock.Today) : ((int, int, string)?)null;
        int? ageYears = w.PatientDateOfBirth is { } dob0 ? AgeOn(dob0, ClinicClock.Today) : null;
        if (isPregnant && aog is { } a1) row.Children.Add(Chip($"Preg {a1.Item1}w", "PrimarySubtle", "Primary", "Primary"));
        if (!isPregnant && showOb && aog is { } a2) row.Children.Add(Chip($"LMP {a2.Item1}w", "PrimarySubtle", "Primary", "Primary"));
        var hasGp = f?.Gravida is not null || f?.Para is not null;
        var gp = $"G{f?.Gravida?.ToString(CultureInfo.InvariantCulture) ?? "?"}/P{f?.Para?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
        if (hasGp) row.Children.Add(Chip(gp, "SurfaceContainerLow", "OnSurfaceVariant", "OutlineVariant"));
        if (ageYears is < 18 && !string.IsNullOrWhiteSpace(f?.GuardianName))
            row.Children.Add(Chip($"Guardian: {f!.GuardianName}{(string.IsNullOrWhiteSpace(f.GuardianRelationship) ? "" : $" ({f.GuardianRelationship})")}", "SurfaceContainerLow", "OnSurfaceVariant", "OutlineVariant"));
        //JJQR - END
        var (bg, fg, border) = strong ? ("StatusDangerSurface", "StatusDanger", "StatusDangerBorder")
            : amber ? ("StatusWarningSurface", "StatusWarning", "StatusWarningBorder")
            : ("SurfaceContainerLow", "OnSurface", "OutlineVariant");
        var chipLabel = ClinicTheme.Text("⚠  Allergies: " + text, "LabelMd");
        chipLabel.TextColor = ClinicTheme.Color(fg);
        var chip = new Border
        {
            BackgroundColor = ClinicTheme.Color(bg), Stroke = ClinicTheme.Color(border), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 8, 4), Content = chipLabel,
        };
        var canEdit = !ReadOnly;
        if (canEdit)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => { _allergyEditorOpen = !_allergyEditorOpen; RenderBanner(); };
            chip.GestureRecognizers.Add(tap);
        }
        row.Children.Add(chip);
        if (canEdit && needsReview)
            row.Children.Add(Btn("Record allergies / NKDA", () => { _allergyEditorOpen = true; RenderBanner(); }, "PrimaryButton").Also(b => b.Margin = new Thickness(0, 0, 8, 4)));
        //JJQR - AI (21 Nov 2026) - ALL-1: "Payment: {status || Not recorded}" last on the row; OB strip under the banner (web lg+ only — the desktop is always wide)
        row.Children.Add(ClinicTheme.Text($"Payment: {(string.IsNullOrWhiteSpace(_paymentDisplay) ? "Not recorded" : _paymentDisplay)}", "LabelSm")
            .Also(p => { p.TextColor = ClinicTheme.Color("OnSurfaceVariant"); p.VerticalOptions = LayoutOptions.Center; }));
        AllergyBar.Children.Add(row);
        if (showOb)
        {
            var ident = new List<string>();
            if (!string.IsNullOrWhiteSpace(w.PatientCode)) ident.Add(w.PatientCode);
            if (!string.IsNullOrWhiteSpace(w.PatientSex)) ident.Add(w.PatientSex!);
            if (w.PatientDateOfBirth is { } dob1) ident.Add($"{dob1:yyyy-MM-dd} ({AgeOn(dob1, ClinicClock.Today)}y)");
            var ob = new FormattedString();
            ob.Spans.Add(new Span
            {
                TextColor = ClinicTheme.Color("Primary"),
                Text = $"OB: {(isPregnant ? "Pregnant" : "LMP recorded")}"
                    + (aog is { } a3 ? $" · AOG {a3.Item3}" : "")
                    + (lmp is { } l1 ? $" · EDD {EddFromLmp(l1)}" : "")
                    + (hasGp ? $" · {gp}" : "")
                    + (string.IsNullOrWhiteSpace(f?.RhFactor) ? "" : $" · Rh {f!.RhFactor}"),
            });
            if (ident.Count > 0) ob.Spans.Add(new Span { Text = " · " + string.Join(" · ", ident), TextColor = ClinicTheme.Color("OnSurfaceVariant") });
            AllergyBar.Children.Add(new Label { Style = ClinicTheme.Style("LabelSm"), FormattedText = ob, Margin = new Thickness(0, 2, 0, 4) });
        }
        //JJQR - END
        if (canEdit && _allergyEditorOpen) AllergyBar.Children.Add(AllergyEditor(nkda, active));
    }

    View AllergyEditor(bool nkda, List<PatientAllergy> active)
    {
        var col = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 4) };
        col.Children.Add(new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Children = { Btn("Done", () => { _allergyEditorOpen = false; RenderBanner(); }, "GhostButton") } });
        var box = new CheckBox { IsChecked = nkda };
        box.CheckedChanged += async (_, e) =>
        {
            if (!e.Value) { _allergyEditorOpen = false; await SaveAllergiesAsync(Array.Empty<AllergyInput>()); return; }
            if (active.Count > 0)
            {
                ShowOverlay("Mark NKDA?", [ClinicTheme.Text("Clear recorded allergies and mark NKDA?", "MutedBody")],
                    ("Cancel", () => { HideOverlay(); RenderBanner(); }, "SecondaryButton"),
                    ("Clear allergies & mark NKDA", () => { HideOverlay(); _allergyEditorOpen = false; _ = SaveAllergiesAsync(new[] { new AllergyInput("NKDA", IsNkda: true) }); }, "PrimaryButton"));
                return;
            }
            _allergyEditorOpen = false;
            await SaveAllergiesAsync(new[] { new AllergyInput("NKDA", IsNkda: true) });
        };
        col.Children.Add(WrapCheck(box, "NKDA"));
        var draft = new Entry { Placeholder = "Add allergy (e.g. Penicillin)", MinimumWidthRequest = 192, HorizontalOptions = LayoutOptions.Fill };
        var add = Btn("Add allergy", () => { }, "SecondaryButton");
        add.IsEnabled = false;
        draft.TextChanged += (_, e) => add.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);
        async Task Submit()
        {
            var name = (draft.Text ?? "").Trim();
            if (name.Length == 0) return;
            var list = active.Select(ToInput).ToList();
            list.Add(new AllergyInput(name));
            _allergyEditorOpen = false;
            await SaveAllergiesAsync(list);
        }
        add.Clicked += async (_, _) => await Submit();
        draft.Completed += async (_, _) => await Submit();
        var addRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        addRow.Add(ClinicTheme.Text("Allergies", "FieldLabel").Also(l => l.VerticalOptions = LayoutOptions.Center), 0);
        addRow.Add(draft, 1);
        addRow.Add(add, 2);
        col.Children.Add(addRow);
        if (!nkda && active.Count > 0)
        {
            var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var a in active)
            {
                var keep = active.Where(x => !ReferenceEquals(x, a)).Select(ToInput).ToList();
                var chipRow = new HorizontalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(FormatAllergen(a.Allergen), "LabelSm").Also(l => l.VerticalOptions = LayoutOptions.Center) } };
                var x = Btn("×", () => { _allergyEditorOpen = false; _ = SaveAllergiesAsync(keep); }, "GhostButton");
                chipRow.Children.Add(x);
                chips.Children.Add(new Border
                {
                    BackgroundColor = ClinicTheme.Color("SurfaceContainerHigh"), StrokeThickness = 0, Padding = new Thickness(10, 0), Margin = new Thickness(0, 0, 6, 6),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Content = chipRow,
                });
            }
            col.Children.Add(chips);
        }
        return new Border { Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, BackgroundColor = ClinicTheme.Color("Surface"), Padding = 12, Content = col };
    }

    // ---------- section indicator (web renderSectionIndicator) ----------
    string StepMark(int i)
    {
        var fields = i switch
        {
            0 => new[] { D.ChiefComplaint, D.Subjective, D.Objective, D.Assessment, D.Plan },
            1 => D.Vitals.Select(v => v.Value).ToArray(),
            6 => D.FollowUpMode switch
            {
                FollowUpMode.Dated => new[] { D.FollowUpDate?.ToString("yyyy-MM-dd"), D.FollowUpReason, D.FollowUpInstructions },
                FollowUpMode.Prn => new[] { D.FollowUpReason, D.FollowUpInstructions },
                _ => new[] { "confirmed" },
            },
            _ => null,
        };
        var required = ConsultRules.RequiredSections.Contains(i);
        var showWarnings = _mode != "view" || _completeAttempted;
        if (fields is { Length: > 0 })
        {
            var filled = fields.Count(f => !string.IsNullOrWhiteSpace(f));
            if (filled == 0) return showWarnings && _completeAttempted && required && !ConsultRules.SectionSatisfied(i, D, PfShown()) ? "⚠" : "○";
            if (filled == fields.Length) return "✓";
            return $"{filled}/{fields.Length}";
        }
        var satisfied = ConsultRules.SectionSatisfied(i, D, PfShown());
        return ConsultSteps.Indicator(satisfied && (required || HasContent(i)), required, HasDraft(i), showWarnings) switch
        {
            StepIndicator.Complete => "✓", StepIndicator.Warning => "⚠", StepIndicator.Partial => "◐", _ => "○",
        };
    }

    /// <summary>web section card title: "{indicator} {n}. {Section}" (n = position among the visible sections).</summary>
    View SectionTitle(int i)
    {
        var order = ConsultRules.VisibleOrder(_showMore, _section).ToList();
        var n = order.IndexOf(i) + 1;
        var title = ClinicTheme.Text($"{StepMark(i)}  {(n > 0 ? n + ". " : "")}{ConsultSteps.Sections[i]}", "HeadlineMd");
        return new VerticalStackLayout
        {
            Spacing = 0, Padding = new Thickness(0, 0, 0, 12),
            Children = { title, new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), Margin = new Thickness(0, 12, 0, 0) } },
        };
    }

    // ---------- footer status (web consultation-save-status) ----------
    string SaveStatusText() =>
        _busy ? "Saving your changes…"
        : _saveFailed ? "Not saved — your entries are still here."
        : _mode == "amend" ? (_amendSavedAt is { } t ? $"Amendment saved {t}" : "Amendment · manual save")
        : Dirty ? "Unsaved changes · auto-save every 30 seconds"
        : _draftSavedAt is { } at ? $"Draft saved {at} PHT"
        : D.ConsultationId is not null ? "Draft loaded · no new changes"
        : "Draft not saved · changes auto-save every 30 seconds";

    // ---------- "Discard changes?" (web requestCancelAmend) ----------
    void ConfirmDiscard(Action discard) =>
        ShowOverlay("Discard changes?", [ClinicTheme.Text("You have unsaved changes to this consultation. Discard them?", "MutedBody")],
            ("Keep Editing", HideOverlay, "SecondaryButton"),
            ("Discard", () => { HideOverlay(); discard(); }, "DangerButton"));

    // ---------- "Confirm Lab Orders" ----------
    void ConfirmLabOrders(bool print)
    {
        var body = new List<View>();
        if (D.LabOrders.Count == 0) body.Add(ClinicTheme.Text("No tests selected.", "MutedBody"));
        foreach (var l in D.LabOrders)
        {
            var reason = l.Reason ?? l.ClinicalIndication;
            var fs = new FormattedString();
            fs.Spans.Add(new Span { Text = l.TestName, TextColor = ClinicTheme.Color("OnSurface") });
            if (!string.IsNullOrWhiteSpace(reason)) fs.Spans.Add(new Span { Text = " — " + reason, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
            body.Add(new Label { Style = ClinicTheme.Style("BodyMd"), FormattedText = fs });
        }
        ShowOverlay("Confirm Lab Orders", body,
            ("Go Back", HideOverlay, "SecondaryButton"),
            (print ? "Confirm & Print" : "Confirm & Save", async () =>
            {
                HideOverlay();
                if (print) await PrintAsync("Print Lab Orders", id => _printDocs.LabRequestAsync(id));
                else await SaveAsync(goNext: false);
            }, "PrimaryButton"));
    }

    // ---------- bold "Label:" lines (web <strong>) ----------
    static Label Strong(string label, string? value, string style = "BodyMd")
    {
        var fs = new FormattedString();
        fs.Spans.Add(new Span { Text = label + ": ", FontAttributes = FontAttributes.Bold });
        fs.Spans.Add(new Span { Text = value ?? "" });
        return new Label { Style = ClinicTheme.Style(style), FormattedText = fs };
    }

    // ---------- justCompleted (web "Consultation saved") ----------
    void ShowJustCompletedWeb()
    {
        var w = _workspace!;
        var ident = new List<string>();
        if (!string.IsNullOrWhiteSpace(w.PatientCode)) ident.Add(w.PatientCode);
        if (!string.IsNullOrWhiteSpace(w.PatientSex)) ident.Add(w.PatientSex!);
        if (w.PatientDateOfBirth is { } dob) ident.Add($"{dob:yyyy-MM-dd} ({AgeOn(dob, ClinicClock.Today)}y)");
        var rxCount = D.Prescriptions.Count(r => !string.IsNullOrWhiteSpace(r.GenericName));
        var labCount = D.LabOrders.Count(l => !string.IsNullOrWhiteSpace(l.TestName));
        var ph = CultureInfo.GetCultureInfo("en-PH");
        var followUp = D.FollowUpMode switch
        {
            FollowUpMode.None => "No follow-up",
            FollowUpMode.Prn => $"PRN — {(string.IsNullOrWhiteSpace(D.FollowUpReason) ? "return if needed" : D.FollowUpReason)}",
            _ => D.FollowUpDate is { } fd ? $"{fd.ToString("MMM d, yyyy", ph)}{(string.IsNullOrWhiteSpace(D.FollowUpReason) ? "" : $" — {D.FollowUpReason}")}" : "Not set",
        };
        var success = ClinicTheme.Text("✓  Consultation saved", "LabelMd");
        success.TextColor = ClinicTheme.Color("StatusSuccess");
        var body = new List<View>
        {
            new Border { StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("StatusSuccessSurface"), Padding = new Thickness(12, 8), Content = success },
            Card(
                Strong("Patient", (string.IsNullOrWhiteSpace(w.PatientName) ? "—" : w.PatientName) + (ident.Count > 0 ? " · " + string.Join(" · ", ident) : "")),
                Strong("Chief Complaint", string.IsNullOrWhiteSpace(D.ChiefComplaint) ? "—" : D.ChiefComplaint),
                Strong("Diagnoses", D.Diagnoses.Count > 0 ? string.Join(", ", D.Diagnoses.Select(DxText)) : "—"),
                Strong("Prescriptions", $"{rxCount} line{(rxCount == 1 ? "" : "s")}"),
                Strong("Lab orders", labCount.ToString(CultureInfo.InvariantCulture)),
                Strong("Follow-up", followUp)),
        };
        var amount = D.PfDecision == "Charge" ? D.PfAmount ?? (w.ComputedFee > 0 ? w.ComputedFee : null) : null;
        body.Add(Row(ClinicTheme.Text(ConsultClinicalAids.ProfessionalFeeLine(D.PfDecision, amount, D.PfWaiveReason, PreviewFee().Total), "BodyMd"),
            Btn("Edit", () => { HideOverlay(); OpenProfessionalFeeForEdit(); }, "GhostButton")));
        // web consultPrintButtons: overlay buttons cannot be disabled here, so only the ones with something to print are shown
        var actions = new List<(string, Action, string)>();
        if (rxCount > 0) actions.Add(("Print Prescription", () => OnPrintRx(null, EventArgs.Empty), "SecondaryButton"));
        if (labCount > 0) actions.Add(("Print Lab Orders", () => OnPrintLabs(null, EventArgs.Empty), "SecondaryButton"));
        actions.Add(("Back to Queue", async () => { HideOverlay(); await Shell.Current.GoToAsync(".."); }, "SecondaryButton"));
        actions.Add(("Return to patient queue", async () => { HideOverlay(); await Shell.Current.GoToAsync(".."); }, "PrimaryButton"));
        ShowOverlay("", body, actions.ToArray());
    }
}
//JJQR - END
