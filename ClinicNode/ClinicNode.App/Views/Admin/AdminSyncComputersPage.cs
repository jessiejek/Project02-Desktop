//JJQR - AI (21 Nov 2026) - Admin "Sync computers" page (code-only, like AdminRegisterNodePage): every computer registered with the clinic
// server, its status, last seen and registered time, with Rotate secret / Revoke, plus a link to "Register this node".
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
//JJQR - AI (21 Nov 2026)
using ClinicNode.Core.Admin;
//JJQR - END

namespace ClinicNode.App.Views.Admin;

public sealed class AdminSyncComputersPage : ContentPage
{
    private readonly AdminSyncComputersViewModel _vm;
    private readonly VerticalStackLayout _list = new() { Spacing = 12 };
    private readonly Label _status = new() { Style = ClinicTheme.Style("MutedBody") };
    private readonly Label _thisComputer = new() { Style = ClinicTheme.Style("MutedSm") };
    private readonly Label _error = new() { Style = ClinicTheme.Style("ErrorText"), IsVisible = false };
    private readonly Border _secretCard;
    private readonly Label _secretTitle = new() { Style = ClinicTheme.Style("LabelMd") };
    private readonly Entry _secretValue = new() { IsReadOnly = true };
    private readonly ActivityIndicator _busy = new() { IsRunning = false, IsVisible = false };
    //JJQR - AI (21 Nov 2026) - offline / empty / error states as cards (same treatment as the web-style admin pages)
    private readonly ContentView _notice = new();
    //JJQR - END

    public AdminSyncComputersPage(AdminSyncComputersViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Sync computers";
        BackgroundColor = ClinicTheme.Color("Background");

        var refresh = new Button { Text = "Refresh", Style = ClinicTheme.Style("SecondaryButton") };
        refresh.Clicked += async (_, _) => await LoadAsync();
        var register = new Button { Text = "Register this node", Style = ClinicTheme.Style("PrimaryButton") };
        register.Clicked += async (_, _) => await AdminSyncComputersViewModel.OpenRegisterAsync();

        var copy = new Button { Text = "Copy", Style = ClinicTheme.Style("SecondaryButton") };
        copy.Clicked += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(_vm.OneTimeSecret)) await Clipboard.Default.SetTextAsync(_vm.OneTimeSecret);
        };
        var hide = new Button { Text = "Done — hide secret", Style = ClinicTheme.Style("GhostButton") };
        hide.Clicked += (_, _) => { _vm.HideSecret(); Render(); };
        _secretCard = new Border
        {
            Style = ClinicTheme.Style("Card"),
            IsVisible = false,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    _secretTitle,
                    ClinicTheme.Text("Shown once. Enter it on that computer (This computer → sync settings). It is not saved here.", "MutedSm"),
                    _secretValue,
                    new HorizontalStackLayout { Spacing = 8, Children = { copy, hide } },
                },
            },
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                MaximumWidthRequest = 960,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                        ColumnSpacing = 12,
                        Children =
                        {
                            new VerticalStackLayout
                            {
                                Spacing = 4,
                                Children =
                                {
                                    ClinicTheme.Text("Sync computers", "HeadlineMd"),
                                    ClinicTheme.Text("Computers that keep a copy of the clinic records and sync with the clinic server.", "MutedBody"),
                                },
                            },
                            Col(new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Start, Children = { refresh, register } }, 1),
                        },
                    },
                    _thisComputer,
                    _status,
                    _notice, //JJQR - AI (21 Nov 2026) //JJQR - END
                    _error,
                    _busy,
                    _secretCard,
                    _list,
                },
            },
        };
    }

    static T Col<T>(T view, int column) where T : BindableObject
    {
        Grid.SetColumn(view, column);
        return view;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var hide = ClinicNode.App.Controls.Skeleton.CoverPage(this); //JJQR - AI (Nov 22, 2026) - skeleton until the first load finishes //JJQR - END
        _ = LoadAsync().ContinueWith(_ => MainThread.BeginInvokeOnMainThread(hide));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.HideSecret();
        Render();
    }

    async Task LoadAsync()
    {
        _busy.IsVisible = _busy.IsRunning = true;
        await _vm.LoadAsync();
        _busy.IsVisible = _busy.IsRunning = false;
        Render();
    }

    void Render()
    {
        _thisComputer.Text = _vm.ThisComputerLine;
        _status.Text = _vm.StatusMessage ?? "";
        _status.IsVisible = _vm.HasStatus;
        _error.Text = _vm.Error ?? "";
        _error.IsVisible = _vm.HasError;
        _secretCard.IsVisible = _vm.HasOneTimeSecret;
        _secretTitle.Text = _vm.HasOneTimeSecret ? $"New secret for {_vm.OneTimeSecretFor}" : "";
        _secretValue.Text = _vm.OneTimeSecret ?? "";

        _list.Children.Clear();
        foreach (var row in _vm.Items) _list.Children.Add(MakeRow(row));

        //JJQR - AI (21 Nov 2026) - states:
        //   offline (local list only) → info banner "needs a connection…"; load failed with nothing to show → "Sync computers unavailable" + Try again;
        //   online but none registered → empty card. The raw status line is hidden for those cases so the text isn't shown twice.
        _notice.Content = null;
        if (_vm.HasError && _vm.Items.Count == 0)
        {
            _error.IsVisible = false;
            var retry = new Button { Text = "Try again", Style = ClinicTheme.Style("SecondaryButton"), HorizontalOptions = LayoutOptions.Start };
            retry.Clicked += async (_, _) => await LoadAsync();
            _notice.Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    ClinicTheme.Text("Sync computers unavailable", "HeadlineSm"),
                    ClinicTheme.Text("The list of computers could not be loaded. Try again.", "MutedBody"),
                    retry,
                },
            };
        }
        else if (!_vm.FromServer)
        {
            _status.IsVisible = false;
            _notice.Content = new Border
            {
                Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(12, 8),
                Content = ClinicTheme.Text(LocalAdminNodeService.NeedsServer + " Only this computer is shown.", "PillLabelInfo"),
            };
        }
        else if (_vm.Items.Count == 0)
        {
            _status.IsVisible = false;
            _notice.Content = new Border
            {
                Style = ClinicTheme.Style("Card"),
                Content = ClinicTheme.Text("No computers are registered for sync yet. Use “Register this node” on each clinic computer.", "MutedBody"),
            };
        }
        //JJQR - END
    }

    View MakeRow(SyncComputerRow row)
    {
        var title = new HorizontalStackLayout { Spacing = 8, Children = { ClinicTheme.Text(row.Name, "HeadlineSm") } };
        if (row.IsThisComputer)
            title.Children.Add(new Border { Style = ClinicTheme.Style("PillInfo"), Content = ClinicTheme.Text("This computer", "PillLabelInfo") });
        title.Children.Add(ClinicTheme.MakeStatusPill(row.Status));

        var info = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                title,
                ClinicTheme.Text(string.IsNullOrWhiteSpace(row.Details) ? "—" : row.Details, "MutedSm"),
                ClinicTheme.Text($"Last seen: {row.LastSeen}", "BodyMd"),
                ClinicTheme.Text($"Registered: {row.Registered}", "MutedSm"),
            },
        };
        //JJQR - AI (21 Nov 2026) - server health: last push, operations and rejected changes in the last 24 hours
        if (!string.IsNullOrEmpty(row.LastPush))
            info.Children.Insert(3, ClinicTheme.Text($"Last push: {row.LastPush}", "BodyMd"));
        var health = new List<string>();
        if (!string.IsNullOrEmpty(row.Operations24h)) health.Add($"Operations (24h): {row.Operations24h}");
        if (!string.IsNullOrEmpty(row.Rejected24h)) health.Add($"Rejected (24h): {row.Rejected24h}");
        if (health.Count > 0)
        {
            var line = ClinicTheme.Text(string.Join("  ·  ", health), "MutedSm");
            if (row.HasRejections) line.TextColor = ClinicTheme.Color("StatusDanger");
            info.Children.Insert(info.Children.Count - 1, line);
        }
        //JJQR - END

        var actions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        if (row.CanManage)
        {
            var rotate = new Button { Text = "Rotate secret", Style = ClinicTheme.Style("SecondaryButton") };
            rotate.Clicked += async (_, _) =>
            {
                var msg = row.IsThisComputer
                    ? "A new secret will be issued and saved on this computer."
                    : $"{row.Name} will stop syncing until the new secret is entered on it.";
                if (!await DisplayAlertAsync("Rotate secret?", msg, "Rotate secret", "Cancel")) return;
                await _vm.RotateAsync(row);
                Render();
            };
            var revoke = new Button { Text = "Revoke", Style = ClinicTheme.Style("DangerButton") };
            revoke.Clicked += async (_, _) =>
            {
                var msg = row.IsThisComputer
                    ? "This computer will no longer be able to sync with the clinic server. It can be registered again later."
                    : $"{row.Name} will no longer be able to sync with the clinic server. This cannot be undone; it would have to be registered again.";
                if (!await DisplayAlertAsync("Revoke this computer?", msg, "Revoke", "Cancel")) return;
                await _vm.RevokeAsync(row);
                Render();
            };
            actions.Children.Add(rotate);
            actions.Children.Add(revoke);
        }

        return new Border
        {
            Style = ClinicTheme.Style("ListCard"),
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 12,
                Children = { info, Col(actions, 1) },
            },
        };
    }
}
//JJQR - END
