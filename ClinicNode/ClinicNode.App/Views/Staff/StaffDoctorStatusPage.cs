//JJQR - AI (Nov 21, 2026) - /staff/doctor-status "Doctor status today": counts, per-doctor Available / Running late / Out today, "Select several" bulk set.
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffDoctorStatusPage : StaffLivePage
{
    static readonly DoctorDayStatusEnum[] Options = [DoctorDayStatusEnum.Available, DoctorDayStatusEnum.RunningLate, DoctorDayStatusEnum.UnavailableToday];
    private readonly FlexLayout _stats = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly VerticalStackLayout _list = new() { Spacing = 0 };
    private readonly ContentView _bulk = new();
    private readonly Label _saveError = StaffUi.Error();
    private readonly Button _selectToggle;
    private readonly HashSet<Guid> _selected = [];
    private bool _selectMode;
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/doctor-status): ErrorState "Doctor status didn’t load" replaces counts + list and hides "Select several"
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    protected override (string Title, string Message)? LoadErrorState => ("Doctor status didn’t load", "Check the connection to the clinic server, then try again.");
    protected override void OnLoadStateChanged(bool failed)
    {
        _body.IsVisible = !failed;
        if (failed) _selectToggle.IsVisible = false;
    }
    //JJQR - END

    public StaffDoctorStatusPage(IServiceProvider sp) : base(sp, "Doctor Status")
    {
        _selectToggle = StaffUi.Btn("Select several", async () =>
        {
            _selectMode = !_selectMode;
            _selected.Clear();
            _selectToggle!.Text = _selectMode ? "Done selecting" : "Select several";
            await ReloadAsync();
        }, "SecondaryButton");
        //JJQR - AI (21 Nov 2026) - STAFF PARITY
        foreach (var v in new View[] { _stats, _saveError, _list, _bulk }) _body.Children.Add(v);
        //JJQR - END
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Doctor status today", "Set who is in, running late or out. Patients see this on the booking screens.", _selectToggle),
                //JJQR - AI (21 Nov 2026) - STAFF PARITY: web order: counts · save error · doctors · "N selected. Set to:" bar (bottom)
                LoadError, ErrorStateView, _body,
                //JJQR - END
            }
        });
    }

    //JJQR - AI (21 Nov 2026) - live updates (doctor status: each doctor's waiting / in-consult counts)
    protected override IReadOnlyList<string> HubEvents => [ClinicNode.Api.Live.ClinicHubEvents.QueueUpdated, ClinicNode.Api.Live.ClinicHubEvents.PatientCheckedIn];
    //JJQR - END

    protected override async Task LoadAsync()
    {
        var b = await Svc<IDoctorStatusService>().GetTodayAsync();
        //JJQR - AI (21 Nov 2026) - web: "Select several" only when there is more than one doctor to apply a status to
        _selectToggle.IsVisible = b.Doctors.Count > 1;
        if (b.Doctors.Count <= 1 && _selectMode) { _selectMode = false; _selected.Clear(); _selectToggle.Text = "Select several"; }
        //JJQR - END
        _stats.Children.Clear();
        _stats.Children.Add(StaffUi.Stat("Available", b.Available.ToString()));
        _stats.Children.Add(StaffUi.Stat("Running late", b.RunningLate.ToString()));
        _stats.Children.Add(StaffUi.Stat("Out today", b.OutToday.ToString()));
        _list.Children.Clear();
        if (b.Doctors.Count == 0) _list.Children.Add(ClinicTheme.Text("No active doctors yet.", "MutedBody"));
        foreach (var d in b.Doctors)
        {
            var doc = d;
            var views = new List<View>();
            if (_selectMode)
            {
                var cb = new CheckBox { IsChecked = _selected.Contains(doc.DoctorId) };
                cb.CheckedChanged += (_, e) => { if (e.Value) _selected.Add(doc.DoctorId); else _selected.Remove(doc.DoctorId); RenderBulk(); };
                views.Add(new HorizontalStackLayout { Spacing = 8, Children = { cb, ClinicTheme.Text(NameOf(doc.Name), "HeadlineSm") } });
            }
            else views.Add(ClinicTheme.Text(NameOf(doc.Name), "HeadlineSm"));
            views.Add(ClinicTheme.MakeStatusPill(doc.Status.ToString()));
            //JJQR - AI (21 Nov 2026) - web: the Available / Running late / Out today choice is hidden while selecting several; tapping the active one does nothing
            if (!_selectMode)
            {
                var choices = Options.Select(o => (View)StaffUi.Btn(DoctorStatusService.Label(o), () => o == doc.Status ? Task.CompletedTask : SetAsync(doc.DoctorId, o),
                    o == doc.Status ? "PrimaryButton" : "SecondaryButton")).ToArray();
                views.Add(StaffUi.Wrap(choices));
            }
            //JJQR - END
            _list.Children.Add(StaffUi.Row(views.ToArray()));
        }
        RenderBulk();
    }

    static string NameOf(string? name) => string.IsNullOrWhiteSpace(name) ? "—" : name; //JJQR - AI (21 Nov 2026) - web: {doc.name || "—"} //JJQR - END

    void RenderBulk()
    {
        _bulk.Content = _selectMode && _selected.Count > 0
            ? StaffUi.Card(
                ClinicTheme.Text($"{_selected.Count} selected. Set to:", "BodyMd"),
                StaffUi.Wrap(
                    StaffUi.Btn("Available", () => BulkAsync(DoctorDayStatusEnum.Available)),
                    StaffUi.Btn("Running late", () => BulkAsync(DoctorDayStatusEnum.RunningLate), "SecondaryButton"),
                    StaffUi.Btn("Out today", () => BulkAsync(DoctorDayStatusEnum.UnavailableToday), "SecondaryButton")))
            : null;
    }

    async Task SetAsync(Guid doctorId, DoctorDayStatusEnum status)
    {
        StaffUi.Show(_saveError, null);
        var r = await Svc<IDoctorStatusService>().SetAsync(doctorId, status);
        if (!r.Succeeded) StaffUi.Show(_saveError, "That status change didn’t save. Please try again.");
        await ReloadAsync();
    }

    async Task BulkAsync(DoctorDayStatusEnum status)
    {
        StaffUi.Show(_saveError, null);
        var r = await Svc<IDoctorStatusService>().SetManyAsync(_selected.ToList(), status);
        if (!r.Succeeded) StaffUi.Show(_saveError, "Some doctors weren’t updated. Refresh to see what saved, then try again.");
        //JJQR - AI (21 Nov 2026) - web: a successful bulk set clears the selection AND leaves select mode; a failure keeps the selection to retry
        if (r.Succeeded)
        {
            _selected.Clear();
            _selectMode = false;
            _selectToggle.Text = "Select several";
        }
        //JJQR - END
        await ReloadAsync();
    }
}
//JJQR - END
