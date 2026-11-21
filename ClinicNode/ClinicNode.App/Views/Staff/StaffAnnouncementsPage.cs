//JJQR - AI (Nov 21, 2026) - /staff/announcements "Clinic announcements" (read-only; admins post/edit).
using ClinicNode.App.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffAnnouncementsPage : StaffLivePage
{
    private readonly VerticalStackLayout _list = new() { Spacing = 12 };
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/announcements): ErrorState "Announcements didn’t load" replaces the list
    protected override (string Title, string Message)? LoadErrorState => ("Announcements didn’t load", "Check the connection to the clinic server, then try again.");
    protected override void OnLoadStateChanged(bool failed) => _list.IsVisible = !failed;
    //JJQR - END

    public StaffAnnouncementsPage(IServiceProvider sp) : base(sp, "Announcements", live: false)
    {
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Clinic announcements", "What patients currently see on their portal. Only admins can post or edit these."),
                LoadError, ErrorStateView, _list, //JJQR - AI (21 Nov 2026) - + ErrorStateView //JJQR - END
            }
        });
    }

    protected override async Task LoadAsync()
    {
        var items = await Svc<IStaffAnnouncementsService>().ListActiveAsync();
        _list.Children.Clear();
        if (items.Count == 0) { _list.Children.Add(ClinicTheme.Text("No announcements are showing right now.", "MutedBody")); return; }
        foreach (var a in items)
            _list.Children.Add(StaffUi.Card(
                ClinicTheme.Text(a.Title, "HeadlineSm"),
                ClinicTheme.Text(a.Body, "BodyMd"),
                //JJQR - AI (21 Nov 2026) - web footer order: date and time · poster
                ClinicTheme.Text($"{StaffUi.DateTimeText(a.PostedDate)} · {a.PostedBy}", "MutedSm")));
                //JJQR - END
    }
}
//JJQR - END
