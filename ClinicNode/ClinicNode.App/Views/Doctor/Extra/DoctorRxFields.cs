//JJQR - AI (21 Nov 2026) - prescription-line fields for the chart prescription form; TemplateLine() mirrors the Templates "Prescription set" block
// (web templates/RxTab.tsx) for reuse by the Templates owner (allen1). Chart form source: (web components/doctor/PrescriptionForm.tsx NewRxTab / EditLineItemModal + RxTimingFields).
// Sig auto-suggest (lib/rx-structured suggestFromSig) is available through FormFields(d, counter, locked, suggestOnRoute).
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

/// <summary>Editable copy of one prescription line (web LineDraft / PrescriptionLineItem).</summary>
public sealed class RxDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid MedicineId { get; set; }
    public string GenericName { get; set; } = "";
    public string Dosage { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Instruction { get; set; } = "";
    public bool IsControlled { get; set; }
    public string Route { get; set; } = "PO";
    public string? Meal { get; set; }
    public List<string> Slots { get; set; } = [];
    public bool? AsNeeded { get; set; }
    public string? DurationKind { get; set; }
    public string DurationValue { get; set; } = "";
    public string Indication { get; set; } = "";

    public static RxDraft From(TplRxLine l) => new()
    {
        MedicineId = l.MedicineId, GenericName = l.GenericName, Dosage = l.Dosage, Quantity = l.Quantity, Instruction = l.Instruction ?? "",
        IsControlled = l.IsControlled, Route = l.Route ?? "", Meal = l.MealRelation, Slots = DoctorRxRules.TimingToSlots(l.Timing).ToList(),
        AsNeeded = l.AsNeeded, DurationKind = l.DurationKind, DurationValue = l.DurationValue is { } v ? v.ToString() : "", Indication = l.Indication ?? "",
    };

    /// <summary>web toItem(): trims, number only for Days / Weeks.</summary>
    public TplRxLine ToLine(string? defaultRoute = null)
    {
        var hasNumber = DurationKind is "Days" or "Weeks";
        var route = Route.Trim();
        return new TplRxLine(MedicineId, GenericName.Trim(), Dosage.Trim(), Quantity.Trim(), Instruction.Trim().Length > 0 ? Instruction.Trim() : null,
            IsControlled, route.Length > 0 ? route : defaultRoute, Meal, DoctorRxRules.SlotsToTiming(Slots), DurationKind,
            hasNumber && int.TryParse(DurationValue, out var n) && n != 0 ? n : null, Indication.Trim().Length > 0 ? Indication.Trim() : null, AsNeeded);
    }

    public RxDraft Clone() => new()
    {
        MedicineId = MedicineId, GenericName = GenericName, Dosage = Dosage, Quantity = Quantity, Instruction = Instruction, IsControlled = IsControlled,
        Route = Route, Meal = Meal, Slots = [.. Slots], AsNeeded = AsNeeded, DurationKind = DurationKind, DurationValue = DurationValue, Indication = Indication,
    };
}

public static class DoctorRxFields
{
    // web RxTimingFields MEALS / Duration kinds / RxTab ROUTES (kept here so this file does not depend on the templates workspace files)
    static readonly string[] Meals = ["Before", "After"];
    static readonly string[] DurationKinds = ["Maintain", "Days", "Weeks"];
    static readonly string[] Routes = ["PO", "IV", "IM", "SC", "SL", "Topical", "Inhaled", "Nasal", "Ophthalmic", "Otic", "PR"];

    /// <summary>Catalog id for an exactly matching generic name (web datalist pick), else Guid.Empty (typed).</summary>
    public static Guid MatchMedicine(IReadOnlyList<TplMedicine> catalog, string name) =>
        catalog.FirstOrDefault(m => string.Equals(m.GenericName, name.Trim(), StringComparison.OrdinalIgnoreCase))?.MedicineId ?? Guid.Empty;

    static Entry Input(string text, string placeholder, Action<string> set)
    {
        var e = new Entry { Text = text, Placeholder = placeholder };
        e.TextChanged += (_, a) => set(a.NewTextValue ?? "");
        return e;
    }

    /// <summary>web RxTimingFields: "Meal timing" Before/After, "Times" slots, "As needed".</summary>
    public static View Timing(RxDraft d, string mealLabel = "Meal timing", string timesLabel = "Times") => Timing(d, null, out _, mealLabel, timesLabel);

    //JJQR - AI (21 Nov 2026) - sig auto-suggest support: <paramref name="touched"/> reports "meal" / "timing" / "asNeeded" when the doctor taps one
    // (web mealTouched / timesTouched / asNeededTouched), <paramref name="refresh"/> re-draws the chips after a suggestion filled them.
    public static View Timing(RxDraft d, Action<string>? touched, out Action refresh, string mealLabel = "Meal timing", string timesLabel = "Times")
    {
        var col = StaffUi.Column(8);
        var meals = StaffUi.Wrap();
        void RenderMeals()
        {
            meals.Children.Clear();
            foreach (var o in Meals)
                meals.Children.Add(DoctorExtraUi.Chip(o, d.Meal == o, () => { d.Meal = d.Meal == o ? null : o; touched?.Invoke("meal"); RenderMeals(); }));
        }
        RenderMeals();
        col.Children.Add(StaffUi.Field(mealLabel, meals));
        var slots = StaffUi.Wrap();
        void RenderSlots()
        {
            slots.Children.Clear();
            foreach (var s in DoctorRxRules.SlotLabels)
                slots.Children.Add(DoctorExtraUi.Chip(s, d.Slots.Contains(s), () =>
                {
                    if (!d.Slots.Remove(s)) d.Slots.Add(s);
                    d.Slots = DoctorRxRules.SlotLabels.Where(d.Slots.Contains).ToList();
                    touched?.Invoke("timing");
                    RenderSlots();
                }));
        }
        RenderSlots();
        col.Children.Add(StaffUi.Field(timesLabel, slots));
        var asNeeded = StaffUi.Wrap();
        void RenderAsNeeded()
        {
            asNeeded.Children.Clear();
            asNeeded.Children.Add(DoctorExtraUi.Chip("As needed", d.AsNeeded == true, () => { d.AsNeeded = d.AsNeeded != true; touched?.Invoke("asNeeded"); RenderAsNeeded(); }));
        }
        RenderAsNeeded();
        col.Children.Add(asNeeded);
        refresh = () => { RenderMeals(); RenderSlots(); RenderAsNeeded(); };
        return col;
    }
    //JJQR - END

    /// <summary>web Duration: Maintain | Days | Weeks (+ "# days" / "# weeks").</summary>
    public static View Duration(RxDraft d)
    {
        var number = new Entry { Text = d.DurationValue, Keyboard = Keyboard.Numeric, WidthRequest = 112 };
        number.TextChanged += (_, a) => d.DurationValue = a.NewTextValue ?? "";
        void Sync()
        {
            number.IsVisible = d.DurationKind is "Days" or "Weeks";
            number.Placeholder = d.DurationKind is { } k ? "# " + k.ToLowerInvariant() : "";
        }
        var row = StaffUi.Wrap();
        void Render()
        {
            row.Children.Clear();
            foreach (var k in DurationKinds)
                row.Children.Add(DoctorExtraUi.Chip(k, d.DurationKind == k, () => { d.DurationKind = d.DurationKind == k ? null : k; Render(); }));
            row.Children.Add(number);
            Sync();
        }
        Render();
        return StaffUi.Field("Duration", row);
    }

    /// <summary>Templates "Prescription set" medicine block (web RxTab fieldset body).</summary>
    public static View TemplateLine(RxDraft d, IReadOnlyList<TplMedicine> catalog)
    {
        var col = StaffUi.Column(8);
        col.Children.Add(StaffUi.Field("Medicine", Input(d.GenericName, "Generic name", v => { d.GenericName = v; d.MedicineId = MatchMedicine(catalog, v); })));
        col.Children.Add(StaffUi.Wrap(
            DoctorExtraUi.Spaced(StaffUi.Field("Dosage", Input(d.Dosage, "500 mg", v => d.Dosage = v))),
            DoctorExtraUi.Spaced(StaffUi.Field("Quantity", Input(d.Quantity, "21 tabs", v => d.Quantity = v)))));
        var route = new Picker { ItemsSource = Routes.ToList() };
        route.SelectedItem = Routes.Contains(d.Route) ? d.Route : null;
        route.SelectedIndexChanged += (_, _) => d.Route = route.SelectedItem as string ?? "";
        col.Children.Add(StaffUi.Field("Route", route));
        col.Children.Add(Timing(d, "Meals", "Timing"));
        col.Children.Add(Duration(d));
        col.Children.Add(StaffUi.Field("Indication", Input(d.Indication, "e.g. for fever", v => d.Indication = v)));
        col.Children.Add(StaffUi.Field("Directions", Input(d.Instruction, "e.g. with water", v => d.Instruction = v)));
        var controlled = new CheckBox { IsChecked = d.IsControlled };
        controlled.CheckedChanged += (_, a) => d.IsControlled = a.Value;
        col.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { controlled, ClinicTheme.Text("Controlled substance", "BodyMd") } });
        return col;
    }

    /// <summary>Prescription form fields after the medication box (web NewRxTab / EditLineItemModal).</summary>
    public static View FormFields(RxDraft d, Label counter) => FormFields(d, counter, null, false);

    //JJQR - AI (21 Nov 2026) - web applySigSuggest: typing the sig (and, on the New tab, the route) fills Meal timing / Times / As needed
    // with suggestFromSig, but only the ones the doctor has not tapped. <paramref name="locked"/> = already-touched keys (null = no suggestions).
    public static View FormFields(RxDraft d, Label counter, HashSet<string>? locked, bool suggestOnRoute)
    {
        var col = StaffUi.Column(8);
        Action refreshTiming = () => { };
        void Suggest()
        {
            if (locked is null) return;
            var (meal, slots, asNeeded) = DoctorRxRules.SuggestFromSig(d.Instruction, d.Route);
            if (!locked.Contains("meal")) d.Meal = meal;
            if (!locked.Contains("timing")) d.Slots = [.. slots];
            if (!locked.Contains("asNeeded")) d.AsNeeded = asNeeded;
            refreshTiming();
        }
        col.Children.Add(StaffUi.Field("Dosage / strength", Input(d.Dosage, "Dosage / strength", v => d.Dosage = v)));
        col.Children.Add(StaffUi.Field("Quantity", Input(d.Quantity, "# Quantity (e.g. 30, 1 box)", v => d.Quantity = v)));
        col.Children.Add(StaffUi.Field("Route", Input(d.Route, "Route (e.g. PO)", v => { d.Route = v; if (suggestOnRoute) Suggest(); })));
        col.Children.Add(Timing(d, k => locked?.Add(k), out var refresh));
        refreshTiming = refresh;
        //JJQR - END
        col.Children.Add(Duration(d));
        col.Children.Add(Input(d.Indication, "Indication (e.g. for fever, for cough)", v => d.Indication = v));
        var sig = DoctorExtraUi.Area(d.Instruction, 64, "Sig. / extra instructions (optional)");
        sig.MaxLength = 200;
        counter.Text = $"{d.Instruction.Length}/200";
        sig.TextChanged += (_, a) => { d.Instruction = a.NewTextValue ?? ""; counter.Text = $"{d.Instruction.Length}/200"; Suggest(); }; //JJQR - AI (21 Nov 2026) suggest //JJQR - END
        col.Children.Add(sig);
        counter.HorizontalTextAlignment = TextAlignment.End;
        col.Children.Add(counter);
        return col;
    }
}
//JJQR - END
