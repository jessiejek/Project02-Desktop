//JJQR - AI (Nov 21, 2026) - badge + list of reprint flags for the staff queue and dashboard. Collapsed to a "N to reprint" badge; tap to open the list (Reprint / Dismiss per flag).
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.Core;
using ClinicNode.Core.Print;
using ClinicNode.Sync;

namespace ClinicNode.App.Views.Staff;

public sealed class ReprintPanel : ContentView
{
    private readonly IServiceProvider _sp;
    private readonly StaffReprintViewModel _vm;
    private bool _open;

    public ReprintPanel(IServiceProvider sp)
    {
        _sp = sp;
        _vm = new StaffReprintViewModel(StaffUi.Get<ISyncStatus>(sp), PrintSlipAsync);
        _vm.Changed += () => MainThread.BeginInvokeOnMainThread(Render);
        Render();
    }

    /// <summary>Call from the host page's LoadAsync / OnAppearing so the badge is fresh.</summary>
    public async Task RefreshAsync()
    {
        try { await _vm.RefreshAsync(); }
        catch { /* sync layer not ready: keep the panel hidden */ }
    }

    async Task PrintSlipAsync(Guid bookingId)
    {
        // Same services as PrintActions / the payments receipt, but errors propagate so a failed print keeps the flag open.
        await ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(_sp, bookingId, withPayment: false); //JJQR - AI (21 Nov 2026) - renumbered online: refresh the row first //JJQR - END
        var doc = await StaffUi.Get<IPrintDocumentFactory>(_sp).QueueSlipAsync(bookingId);
        var printer = Handler?.MauiContext?.Services.GetService<IPrinter>()
            ?? Application.Current?.Handler?.MauiContext?.Services.GetService<IPrinter>()
            ?? throw new InvalidOperationException("The printer service is not available.");
        await printer.PrintAsync(doc);
    }

    void Render()
    {
        if (_vm.Count == 0) { Content = null; IsVisible = false; return; }
        IsVisible = true;
        var col = new VerticalStackLayout { Spacing = 8 };
        var badge = StaffUi.Btn(_vm.BadgeText + (_open ? "  ▲" : "  ▼"), () => { _open = !_open; Render(); return Task.CompletedTask; }, "SecondaryButton");
        col.Children.Add(badge);
        if (_open)
        {
            if (_vm.Error is { Length: > 0 } err) { var l = StaffUi.Error(); StaffUi.Show(l, err); col.Children.Add(l); }
            foreach (var item in _vm.Items) col.Children.Add(Row(item));
        }
        Content = StaffUi.Card(col);
    }

    View Row(ReprintItem item)
    {
        var buttons = new List<View>();
        if (item.CanReprint) buttons.Add(StaffUi.Btn("Reprint", () => _vm.ReprintAsync(item)));
        if (item.CanOpenPatient && item.PatientId is { } pid && FindPage() is { } page)
            buttons.Add(StaffUi.Btn("Open patient", () => StaffUi.PushAsync<StaffPatientDetailPage>(page, pid), "SecondaryButton"));
        buttons.Add(StaffUi.Btn(item.CanReprint ? "Dismiss" : "Mark reprinted", () => _vm.DismissAsync(item.FlagId), "GhostButton"));
        foreach (var b in buttons) b.Margin = new Thickness(0, 0, 8, 0);
        return StaffUi.Row(
            ClinicTheme.Text(item.Title, "HeadlineSm"),
            ClinicTheme.Text(item.Detail, "MutedSm"),
            StaffUi.Wrap(buttons.ToArray()));
    }

    Page? FindPage()
    {
        Element? e = this;
        while (e is not null and not Page) e = e.Parent;
        return e as Page;
    }
}
//JJQR - END
