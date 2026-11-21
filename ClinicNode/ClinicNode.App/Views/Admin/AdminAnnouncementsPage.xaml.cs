//JJQR - AI (21 Nov 2026) - web /admin/announcements layout (see AdminAnnouncementsPage.xaml)
using ClinicApp.Domain.Entities;
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public partial class AdminAnnouncementsPage : ContentPage
{
    readonly AdminAnnouncementsViewModel _vm;
    bool _loaded;

    public AdminAnnouncementsPage(AdminAnnouncementsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render();
        await _vm.LoadAsync();
        _loaded = true;
        Render();
    }

    void Render()
    {
        Root.Children.Clear();
        var description = !_loaded || _vm.LoadFailed
            ? "Notices shown on the patient and staff dashboards."
            : $"Notices shown on the patient and staff dashboards. {_vm.LiveCount} of {_vm.Raw.Count} currently shown.";
        Root.Children.Add(ClinicUi.PageHeader("Announcements", description, ClinicUi.Button("+ New announcement", () => EditAsync(null))));
        if (!string.IsNullOrWhiteSpace(_vm.ListError)) Root.Children.Add(ClinicUi.Alert(_vm.ListError, success: false));

        if (!_loaded) { Root.Children.Add(new ActivityIndicator { IsRunning = true }); return; }
        //JJQR - AI (21 Nov 2026) - web: ErrorState whenever the list load failed; Try again goes back to the loading state first
        if (_vm.LoadFailed)
        {
            Root.Children.Add(ClinicTheme.Text("Announcements unavailable", "HeadlineSm"));
            Root.Children.Add(ClinicTheme.Text("Announcements could not be loaded. Try again.", "MutedBody"));
            Root.Children.Add(ClinicUi.Button("Try again", async () => { _loaded = false; Render(); await _vm.LoadAsync(); _loaded = true; Render(); }, ButtonKind.Secondary));
            return;
        }
        //JJQR - END
        if (_vm.Raw.Count == 0)
        {
            Root.Children.Add(new Border
            {
                Style = ClinicTheme.Style("Card"),
                Content = new VerticalStackLayout
                {
                    Spacing = 16,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "No announcements yet. Post one to let patients know about holidays or schedule changes.", Style = ClinicTheme.Style("MutedBody"), HorizontalTextAlignment = TextAlignment.Center },
                        ClinicUi.Button("New announcement", () => EditAsync(null)),
                    },
                },
            });
            return;
        }
        foreach (var a in _vm.Raw) Root.Children.Add(Card(a));
    }

    View Card(Announcement a)
    {
        var text = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = a.Title, Style = ClinicTheme.Style("HeadlineSm"), LineBreakMode = LineBreakMode.WordWrap },
                new Label { Text = a.Body, Style = ClinicTheme.Style("MutedBody"), MaxLines = 3, LineBreakMode = LineBreakMode.TailTruncation },
                ClinicTheme.Text($"Posted {AdminViewModelBase.ClinicDateShort(DateOnly.FromDateTime(a.CreatedAt.ToOffset(ClinicApp.Domain.ClinicClock.Offset).DateTime))}", "MutedSm"),
            },
        };
        var toggle = new Switch { IsToggled = a.IsActive, OnColor = ClinicTheme.Color("Primary"), IsEnabled = _vm.TogglingId != a.Id, VerticalOptions = LayoutOptions.Center };
        toggle.Toggled += async (_, e) =>
        {
            if (e.Value == a.IsActive) return;
            toggle.IsEnabled = false;
            await _vm.ToggleWebAsync(a);
            Render();
        };
        var shown = ClinicTheme.Text(a.IsActive ? "Shown" : "Hidden", "LabelMd");
        shown.VerticalOptions = LayoutOptions.Center;
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
        top.Add(text, 0, 0);
        top.Add(new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Start, Children = { toggle, shown } }, 1, 0);

        var del = ClinicUi.Button("Delete", () => DeleteAsync(a), ButtonKind.Ghost);
        del.TextColor = ClinicTheme.Color("StatusDanger");
        var actions = new HorizontalStackLayout { Spacing = 8, Children = { ClinicUi.Button("Edit", () => EditAsync(a), ButtonKind.Secondary), del } };

        return new Border
        {
            Style = ClinicTheme.Style("Card"),
            BackgroundColor = a.IsActive ? ClinicTheme.Color("SurfaceContainerLowest") : ClinicTheme.Color("SurfaceContainerLow"),
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children = { top, new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") }, actions },
            },
        };
    }

    //JJQR - AI (21 Nov 2026) - web Modals: Save / Delete run while the modal stays open (Cancel / Keep it disabled while saving) and a failure
    // shows the FormAlert inside the open modal; the save button stays disabled until Title and Message are filled.
    async Task EditAsync(Announcement? a)
    {
        var title = new Entry { Text = a?.Title ?? "" };
        var body = new Editor { Text = a?.Body ?? "", HeightRequest = 120 };
        var active = new CheckBox { IsChecked = a?.IsActive ?? true, Color = ClinicTheme.Color("Primary") };
        var form = new VerticalStackLayout { Spacing = 16 };
        form.Children.Add(ClinicUi.Field("Title", title));
        form.Children.Add(ClinicUi.Field("Message", body));
        form.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { active, new Label { Text = "Show on dashboards now", Style = ClinicTheme.Style("BodyMd"), VerticalOptions = LayoutOptions.Center } } });
        // web: the new-announcement confirm label follows the checkbox live ("Post announcement" / "Save as hidden")
        string Label() => a is null ? (active.IsChecked ? "Post announcement" : "Save as hidden") : "Save changes";
        bool Filled() => !string.IsNullOrWhiteSpace(title.Text) && !string.IsNullOrWhiteSpace(body.Text);
        var saved = await AdminDialog.ShowSubmitAsync(this, a is null ? "New announcement" : "Edit announcement", form, Label(),
            async () => Filled() ? await _vm.SaveWebAsync(a?.Id, title.Text!, body.Text!, active.IsChecked) : "",
            onOpen: dialog =>
            {
                dialog.ConfirmButton.IsEnabled = Filled();
                active.CheckedChanged += (_, _) => dialog.ConfirmButton.Text = Label();
                title.TextChanged += (_, _) => dialog.ConfirmButton.IsEnabled = Filled();
                body.TextChanged += (_, _) => dialog.ConfirmButton.IsEnabled = Filled();
            });
        if (saved) Render();
    }

    async Task DeleteAsync(Announcement a)
    {
        var body = new VerticalStackLayout { Spacing = 16 };
        body.Children.Add(ClinicTheme.Text($"\u201c{a.Title}\u201d will be removed for everyone. This can't be undone; hide it instead if you may need it again.", "MutedBody"));
        var deleted = await AdminDialog.ShowSubmitAsync(this, "Delete announcement?", body, "Delete", () => _vm.DeleteWebAsync(a.Id), cancel: "Keep it", danger: true);
        if (deleted) Render();
    }
    //JJQR - END
}
//JJQR - END
