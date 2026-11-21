//JJQR - AI (21 Nov 2026) - Admin "Receipt number clashes" page (code-only, like AdminSyncComputersPage). Read-only; when offline it only shows a
// short "needs internet" note. Lives in the System group next to Sync computers (route admin-or-clashes).
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public sealed class AdminOrClashesPage : ContentPage
{
    readonly AdminOrClashesViewModel _vm;
    readonly VerticalStackLayout _root = new() { Padding = 24, Spacing = 24 };

    public AdminOrClashesPage(AdminOrClashesViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Receipt number clashes";
        BackgroundColor = ClinicTheme.Color("Background");
        Content = new ScrollView { Content = _root };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render();
        await _vm.LoadAsync();
        Render();
    }

    void Render()
    {
        _root.Children.Clear();
        var refresh = ClinicUi.Button("Refresh", async () => { await _vm.LoadAsync(); Render(); }, ButtonKind.Secondary);
        refresh.IsEnabled = _vm.Loaded;
        _root.Children.Add(ClinicUi.PageHeader("Receipt number clashes",
            "OR numbers that were recorded on more than one payment, for example by two computers while offline. Read-only.", refresh));

        if (!_vm.Loaded) { _root.Children.Add(new ActivityIndicator { IsRunning = true }); return; }
        if (_vm.LoadFailed)
        {
            _root.Children.Add(ClinicTheme.Text("Clashes unavailable", "HeadlineSm"));
            _root.Children.Add(ClinicTheme.Text("The list could not be loaded. Try again.", "MutedBody"));
            _root.Children.Add(ClinicUi.Button("Try again", async () => { await _vm.LoadAsync(); Render(); }, ButtonKind.Secondary));
            return;
        }
        if (!_vm.FromServer)
        {
            _root.Children.Add(new Border
            {
                Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(12, 8),
                Content = ClinicTheme.Text(LocalAdminOrClashService.NeedsInternet, "PillLabelInfo"),
            });
            return;
        }
        if (_vm.Items.Count == 0)
        {
            _root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = ClinicTheme.Text("No clashes. Every receipt (OR) number is used by one payment only.", "MutedBody") });
            return;
        }

        foreach (var c in _vm.Items)
        {
            var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            head.Add(new Label { Text = c.OrNumber, Style = ClinicTheme.Style("HeadlineSm"), FontFamily = "Courier New" }, 0, 0);
            head.Add(AdminPill.Make("Clash", "Warning", c.CountLabel), 1, 0);
            var columns = new List<TableColumn>
            {
                new("Status", GridLength.Auto, o => AdminPill.Make(((OrClashPaymentRow)o).Status)),
                new("Amount", new GridLength(120), o => new Label { Text = ((OrClashPaymentRow)o).Amount, Style = ClinicTheme.Style("TableCell"), HorizontalTextAlignment = TextAlignment.End }),
                new("Confirmed", new GridLength(1.2, GridUnitType.Star), o => ClinicUi.Cell(((OrClashPaymentRow)o).Confirmed)),
                new("Recorded on", GridLength.Star, o => ClinicUi.Cell(((OrClashPaymentRow)o).Origin)),
                new("", new GridLength(130), o =>
                {
                    var open = new Button { Text = "Open booking", Style = ClinicTheme.Style("GhostButton"), HorizontalOptions = LayoutOptions.End };
                    var id = ((OrClashPaymentRow)o).BookingId;
                    open.Clicked += async (_, _) => await _vm.OpenBookingAsync(id);
                    return open;
                }),
            };
            _root.Children.Add(new Border
            {
                Style = ClinicTheme.Style("Card"),
                Content = new VerticalStackLayout { Spacing = 16, Children = { head, ClinicUi.Table(columns, c.Payments.Cast<object>(), "") } },
            });
        }
    }
}
//JJQR - END
