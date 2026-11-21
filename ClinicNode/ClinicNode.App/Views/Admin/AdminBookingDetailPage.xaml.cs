// JJQR - AI (Nov 21, 2026)
using ClinicNode.App.ViewModels.Admin;
//JJQR - AI (21 Nov 2026)
using System.ComponentModel;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
//JJQR - END

namespace ClinicNode.App.Views.Admin;

public partial class AdminBookingDetailPage : ContentPage
{
    //JJQR - AI (21 Nov 2026) - pills + timeline are drawn here (ClinicTheme.MakeStatusPill / web BookingTimeline)
    readonly AdminBookingDetailViewModel _vm;
    //JJQR - END

    public AdminBookingDetailPage(AdminBookingDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        //JJQR - AI (21 Nov 2026)
        _vm = vm;
        _vm.PropertyChanged += OnVmChanged;
        _vm.OpenModal = ShowModalAsync;
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026)
    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AdminBookingDetailViewModel.StatusKey) or nameof(AdminBookingDetailViewModel.PaymentKey) or nameof(AdminBookingDetailViewModel.TimelineIndex))
            MainThread.BeginInvokeOnMainThread(Redraw);
    }

    // Web /admin/bookings/[id] modals, same titles, labels, buttons and enable rules; a failed write keeps the modal open with the error inside.
    Task ShowModalAsync(string key) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        static Editor Area(string? placeholder = null) => new() { HeightRequest = 84, AutoSize = EditorAutoSizeOption.Disabled, Placeholder = placeholder ?? "" };
        static Label Muted(string text) => ClinicTheme.Text(text, "MutedBody");
        switch (key)
        {
            case "reject":
            case "cancel":
            {
                var isCancel = key == "cancel";
                var reason = Area();
                var body = new VerticalStackLayout { Spacing = 12, Children = { Muted("The booking will end as Cancelled. Your reason is kept on the booking."), ClinicUi.Field("Reason", reason, isCancel ? "At least 5 characters." : null) } };
                var title = isCancel ? "Cancel this booking?" : _vm.Status == ClinicApp.Domain.Enums.BookingStatus.ProofSubmitted ? "Reject this payment proof?" : "Reject this booking?";
                await AdminDialog.ShowSubmitAsync(this, title, body, isCancel ? "Cancel booking" : "Reject",
                    () => _vm.SubmitReasonAsync(isCancel, reason.Text ?? ""), cancel: "Back", danger: true,
                    onOpen: d =>
                    {
                        void Sync() => d.ConfirmButton.IsEnabled = isCancel ? (reason.Text ?? "").Trim().Length >= 5 : !string.IsNullOrWhiteSpace(reason.Text);
                        reason.TextChanged += (_, _) => Sync();
                        Sync();
                    });
                break;
            }
            case "noshow":
            {
                var reason = Area();
                var body = new VerticalStackLayout { Spacing = 12, Children = { Muted("Mark this appointment as a no-show?"), ClinicUi.Field("Reason (optional)", reason) } };
                await AdminDialog.ShowSubmitAsync(this, "Mark as no-show?", body, "Mark no-show", () => _vm.SubmitNoShowAsync(reason.Text), cancel: "Back", danger: true);
                break;
            }
            case "waive":
            {
                var reason = Area();
                var body = new VerticalStackLayout { Spacing = 12, Children = { ClinicUi.Field("Reason", reason) } };
                await AdminDialog.ShowSubmitAsync(this, _vm.WaiveTitle, body, "Waive payment", () => _vm.SubmitWaiveAsync(reason.Text ?? ""), cancel: "Cancel",
                    onOpen: d =>
                    {
                        void Sync() => d.ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(reason.Text);
                        reason.TextChanged += (_, _) => Sync();
                        Sync();
                    });
                break;
            }
            case "refund":
            {
                var amount = new Entry { Keyboard = Keyboard.Numeric, Placeholder = _vm.RefundAmountPlaceholder };
                var reason = Area();
                var body = new VerticalStackLayout { Spacing = 12, Children = { ClinicUi.Field("Amount (₱)", amount), ClinicUi.Field("Reason", reason) } };
                await AdminDialog.ShowSubmitAsync(this, "Refund this payment?", body, "Refund", () => _vm.SubmitRefundAsync(amount.Text ?? "", reason.Text ?? ""), cancel: "Cancel", danger: true,
                    onOpen: d =>
                    {
                        void Sync() => d.ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(reason.Text) && !string.IsNullOrWhiteSpace(amount.Text);
                        reason.TextChanged += (_, _) => Sync();
                        amount.TextChanged += (_, _) => Sync();
                        Sync();
                    });
                break;
            }
            case "reschedule":
            {
                var date = new DatePicker { Date = DateTime.Today, Format = "MMM d, yyyy" };
                var reason = Area("Reason (optional)");
                var body = new VerticalStackLayout { Spacing = 12, Children = { ClinicUi.Field("New appointment date", ClinicPickers.DateBox(null, date, null, true)), reason } };
                await AdminDialog.ShowSubmitAsync(this, "Reschedule Booking", body, "Confirm Reschedule",
                    () => _vm.SubmitRescheduleAsync(date.Date ?? DateTime.Today, reason.Text), cancel: "Back", busyText: "Saving…");
                break;
            }
        }
    });

    void Redraw()
    {
        PillRow.Children.Clear();
        if (!string.IsNullOrEmpty(_vm.StatusKey)) PillRow.Children.Add(ClinicTheme.MakeStatusPill(_vm.StatusKey));
        if (_vm.PaymentKey is { Length: > 0 } pay) PillRow.Children.Add(ClinicTheme.MakeStatusPill(pay));

        var steps = AdminBookingDetailViewModel.TimelineSteps;
        var current = _vm.TimelineIndex;
        TimelineCard.IsVisible = current >= 0;
        TimelineGrid.Children.Clear();
        TimelineGrid.ColumnDefinitions.Clear();
        if (current < 0) return;
        for (var i = 0; i < steps.Length; i++)
        {
            TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var done = i < current;
            var active = i == current;
            var color = done || active ? ClinicTheme.Color("Primary") : ClinicTheme.Color("OutlineVariant");
            var dot = new Border
            {
                WidthRequest = 28,
                HeightRequest = 28,
                StrokeThickness = active ? 0 : 2,
                Stroke = color,
                BackgroundColor = done || active ? color : Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                HorizontalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = done ? "✓" : (i + 1).ToString(),
                    TextColor = done || active ? Colors.White : ClinicTheme.Color("OnSurfaceVariant"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    FontSize = 12,
                },
            };
            var label = ClinicTheme.Text(ClinicTheme.StatusLabel(steps[i]), active ? "LabelMd" : "MutedSm");
            label.HorizontalTextAlignment = TextAlignment.Center;
            var cell = new VerticalStackLayout { Spacing = 6, Children = { dot, label } };
            Grid.SetColumn(cell, i);
            TimelineGrid.Children.Add(cell);
        }
    }
    //JJQR - END
}
// JJQR - END
