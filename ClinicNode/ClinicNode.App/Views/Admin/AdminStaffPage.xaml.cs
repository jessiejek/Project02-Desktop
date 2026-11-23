//JJQR - AI (21 Nov 2026) - web /admin/staff layout (see AdminStaffPage.xaml)
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public partial class AdminStaffPage : ContentPage
{
    readonly AdminStaffViewModel _vm;
    readonly Entry _name = new();
    readonly Entry _email = new() { Keyboard = Keyboard.Email };
    readonly Button _send = new() { Text = "Send invite", Style = ClinicTheme.Style("PrimaryButton"), VerticalOptions = LayoutOptions.End };
    readonly VerticalStackLayout _inviteAlert = new();
    readonly VerticalStackLayout _team = new() { Spacing = 16 };

    public AdminStaffPage(AdminStaffViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _name.TextChanged += (_, e) => { _vm.InviteName = e.NewTextValue ?? ""; _send.IsEnabled = _vm.CanSendInvite; };
        _email.TextChanged += (_, e) => { _vm.InviteEmail = e.NewTextValue ?? ""; _send.IsEnabled = _vm.CanSendInvite; };
        _send.IsEnabled = false;
        _send.Clicked += async (_, _) =>
        {
            _send.IsEnabled = false;
            _send.Text = "Sending invite…";
            var ok = await _vm.SendWebInviteAsync();
            _send.Text = "Send invite";
            if (ok)
            {
                _name.Text = ""; _email.Text = "";
                ClinicToast.Show($"Invite sent at {DateTime.Now:h:mm:ss tt}.", ToastKind.Success);
            }
            _send.IsEnabled = _vm.CanSendInvite;
            Render();
        };

        var fields = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 16,
        };
        fields.Add(ClinicUi.Field("Full name", _name), 0, 0);
        fields.Add(ClinicUi.Field("Email", _email), 1, 0);
        fields.Add(_send, 2, 0);

        Root.Children.Add(ClinicUi.PageHeader("Front-desk staff",
            "Invite receptionists and manage who can sign in to the staff portal. Doctors are managed under Doctors."));
        Root.Children.Add(new Border
        {
            Style = ClinicTheme.Style("Card"),
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 4,
                        Children =
                        {
                            ClinicTheme.Text("Invite a staff member", "HeadlineSm"),
                            ClinicTheme.Text("They get an email to set a password; the account shows as Invited until their first login.", "FieldHint"),
                        },
                    },
                    _inviteAlert,
                    fields,
                },
            },
        });
        Root.Children.Add(new VerticalStackLayout { Spacing = 16, Children = { ClinicTheme.Text("Team", "HeadlineMd"), _team } });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClinicNode.App.Controls.Skeleton.WrapAdminPage(this); //JJQR - AI (Nov 22, 2026) - skeleton until the first load finishes //JJQR - END
        await _vm.LoadAsync();
        Render();
    }

    void Render()
    {
        _inviteAlert.Children.Clear();
        if (!string.IsNullOrWhiteSpace(_vm.InviteError)) _inviteAlert.Children.Add(ClinicUi.Alert(_vm.InviteError, success: false));

        _team.Children.Clear();
        if (!string.IsNullOrWhiteSpace(_vm.RowError)) _team.Children.Add(ClinicUi.Alert(_vm.RowError, success: false));
        if (_vm.HasError && _vm.Items.Count == 0)
        {
            //JJQR - AI (21 Nov 2026) - web ErrorState: title "Staff list unavailable", message, Try again (was a bare alert without the title)
            _team.Children.Add(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    ClinicTheme.Text("Staff list unavailable", "HeadlineSm"),
                    ClinicTheme.Text("The staff list could not be loaded. Try again.", "MutedBody"),
                    ClinicUi.Button("Try again", async () => { await _vm.LoadAsync(); Render(); }, ButtonKind.Secondary),
                },
            });
            //JJQR - END
            return;
        }
        var columns = new List<TableColumn>
        {
            new("Name", GridLength.Star, o => ClinicTheme.Text(((StaffListRow)o).Name, "LabelLg")),
            new("Email", new GridLength(2, GridUnitType.Star), o => new Label { Text = ((StaffListRow)o).Email, Style = ClinicTheme.Style("TableCell"), LineBreakMode = LineBreakMode.CharacterWrap }),
            new("Status", GridLength.Auto, o => AdminPill.Make(((StaffListRow)o).StatusKey)),
            new("Action", GridLength.Auto, o => ActionFor((StaffListRow)o)),
        };
        _team.Children.Add(ClinicUi.Table(columns, _vm.Items.Cast<object>(), "No staff yet. Send an invite above."));
    }

    View ActionFor(StaffListRow row)
    {
        var busyOther = _vm.BusyId is { } id && id != row.StaffId;
        Button b;
        if (row.StatusKey == "Invited")
        {
            b = ClinicUi.Button("Revoke invite", async () => { await _vm.RevokeWebAsync(row); Render(); }, ButtonKind.Ghost);
            b.TextColor = ClinicTheme.Color("StatusDanger");
        }
        else
        {
            b = ClinicUi.Button(row.StatusKey == "Active" ? "Deactivate" : "Activate", async () => { await _vm.ToggleStatusAsync(row); Render(); }, ButtonKind.Secondary);
        }
        b.IsEnabled = !busyOther;
        b.HorizontalOptions = LayoutOptions.End;
        //JJQR - AI (Nov 23, 2026) - admin can set a staff member's password (not for pending invites)
        if (row.StatusKey != "Invited")
        {
            var pwd = ClinicUi.Button("Set password", async () => await AdminSetPasswordDialog.ShowAsync(this, row.StaffId, row.Name), ButtonKind.Secondary);
            pwd.IsEnabled = !busyOther;
            return new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { pwd, b } };
        }
        //JJQR - END
        return b;
    }
}
//JJQR - END
