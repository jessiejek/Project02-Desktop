//JJQR - AI (Nov 21, 2026) - "Record payment of ₱X" dialog: Method, Amount received (₱) prefilled with the fee and checked to be EXACT, Reference number, Notes; OR number derived from the booking.
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class RecordPaymentPage : ContentPage
{
    static readonly (PaymentMethod Method, string Label)[] Methods =
        [(PaymentMethod.Cash, "Cash"), (PaymentMethod.GCash, "GCash"), (PaymentMethod.Maya, "Maya"), (PaymentMethod.BankTransfer, "Bank transfer")];

    private readonly TaskCompletionSource<decimal?> _done = new();

    RecordPaymentPage(IStaffPaymentsService svc, Guid bookingId, string patientName, string patientCode, decimal amountDue, bool receiptHint, bool bookletNumber = false)
    {
        Title = "Record payment";
        BackgroundColor = ClinicTheme.Color("Background");
        var method = new Picker { ItemsSource = Methods.Select(m => m.Label).ToList(), SelectedIndex = 0 };
        var amount = new Entry { Text = amountDue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Keyboard = Keyboard.Numeric };
        var reference = new Entry();
        var notes = new Entry();
        var error = StaffUi.Error();
        amount.TextChanged += (_, _) => StaffUi.Show(error, null);
        //JJQR - AI (21 Nov 2026) - 409 or_number_duplicate: an OR field appears prefilled with the clashing number; the retry goes ONLINE ONLY with the typed
        // OR (trimmed, required, max 100). Nothing is saved locally on a clash; a session that expired mid-way asks to sign in again (no local fallback).
        var orEntry = new Entry { MaxLength = ClinicNode.Api.Services.ApiStaffPaymentsService.OrNumberMaxLength, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
        var orField = StaffUi.Field("OR number", orEntry, "This OR number is already used on another payment. Type the OR number printed on this receipt.");
        orField.IsVisible = false;
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.4 (D6): the doctor types the number printed in the receipt booklet; the field is there from the start and required
        if (bookletNumber)
        {
            orField = StaffUi.Field("Receipt number (from your booklet)", orEntry, "Type the number printed on the booklet receipt you hand to the patient.");
            orEntry.Placeholder = "e.g. 000123";
        }
        //JJQR - END
        orEntry.TextChanged += (_, _) => StaffUi.Show(error, null);
        //JJQR - END

        Button? recordButton = null; //JJQR - AI (21 Nov 2026) //JJQR - END
        var record = StaffUi.Btn("Record payment", async () =>
        {
            StaffUi.Show(error, null);
            var check = StaffRules.CheckAmountReceived(amount.Text, amountDue);
            if (!check.Ok) { StaffUi.Show(error, check.Error); return; }
            //JJQR - AI (21 Nov 2026) - typed-OR retry (online only) once the OR field is showing
            var input = new RecordPaymentInput(bookingId, Methods[Math.Max(0, method.SelectedIndex)].Method, amount.Text, reference.Text, notes.Text);
            StaffResult r;
            if (bookletNumber)
            {
                var (booklet, bookletError) = ClinicNode.Api.Services.ApiStaffPaymentsService.CheckOrNumber(orEntry.Text);
                if (booklet is null) { StaffUi.Show(error, "Type the receipt number from your booklet."); return; }
                orEntry.Text = booklet;
                r = await svc.RecordAsync(input with { OrNumber = booklet }); // this computer's own copy when Offline, the clinic server when Online; a number already used is refused either way
            }
            else if (orField.IsVisible)
            {
                var (or, orError) = ClinicNode.Api.Services.ApiStaffPaymentsService.CheckOrNumber(orEntry.Text);
                if (or is null) { StaffUi.Show(error, orError); return; }
                orEntry.Text = or;
                var services = IPlatformApplication.Current?.Services;
                if (services is null) { StaffUi.Show(error, "The payment was not recorded. Please try again."); return; }
                r = await ClinicNode.Api.Services.ApiStaffPaymentsService.Create(services).RecordWithOrAsync(input, or);
            }
            else r = await svc.RecordAsync(input);
            if (!r.Succeeded && !bookletNumber && ClinicNode.Api.Services.PaymentOutcome.TryGetOrClash(r, out var clash))
            {
                orEntry.Text = clash.OrNumber;
                orField.IsVisible = true;
                if (recordButton is not null) recordButton.Text = "Record with this OR number";
                orEntry.Focus();
            }
            //JJQR - END
            if (!r.Succeeded) { StaffUi.Show(error, r.Error ?? "The payment was not recorded. Please try again."); return; }
            _done.TrySetResult(check.Value);
            await Navigation.PopModalAsync();
        });
        recordButton = record as Button; //JJQR - AI (21 Nov 2026) //JJQR - END
        var back = StaffUi.Btn("Back", async () => { _done.TrySetResult(null); await Navigation.PopModalAsync(); }, "SecondaryButton");
        var who = string.IsNullOrWhiteSpace(patientCode) ? patientName : $"{patientName} · {patientCode}";

        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        var body = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(14, 10), Content = new VerticalStackLayout { Spacing = 2, Children = { ClinicTheme.Text(who, "LabelMd"), ClinicTheme.Text($"Amount due {StaffRules.Peso(amountDue)}", "HeadlineSm") } } },
                    error,
                    StaffUi.Field("Method", ClinicPickers.PickerBox(null, method)),
                    StaffUi.Field("Amount received (₱)", amount, $"Collect exactly {StaffRules.Peso(amountDue)} — the amount the doctor set."),
                    StaffUi.Field("Reference number (optional)", reference, receiptHint ? "Shown on the printed receipt." : null),
                    StaffUi.Field("Notes (optional)", notes),
                    orField, //JJQR - AI (21 Nov 2026) - shown only after 409 or_number_duplicate (always shown for the doctor's booklet number, step 2.4) //JJQR - END
                }
            };
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { back, record } };
        Content = ClinicNode.App.Controls.ClinicModal.Build("Record payment", body, footer, async () => { _done.TrySetResult(null); await Navigation.PopModalAsync(); }, 480);
    }

    /// <summary>Returns the amount recorded, or null when the desk backed out.</summary>
    public static async Task<decimal?> ShowAsync(Page from, IStaffPaymentsService svc, Guid bookingId, string patientName, string patientCode, decimal amountDue, bool receiptHint = false, bool bookletNumber = false)
    {
        var p = new RecordPaymentPage(svc, bookingId, patientName, patientCode, amountDue, receiptHint, bookletNumber);
        await from.Navigation.PushModalAsync(p);
        return await p._done.Task;
    }
}
//JJQR - END
