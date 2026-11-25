//JJQR - AI (2026-11-25) - why: GAV-4 (owner approved the pro mockup, "apply it also in web please"). "⚠ Not finished on an earlier day · N patients" inside
// the queue card, under today's queue and above Needs attention: one header per earlier date (most recent open, older closed), the same columns as the
// queue plus a date chip, and three actions per row: Continue (solid teal), Move to today (outline), Mark no-show (red link). Used by the doctor Today,
// doctor Appointments → Today and the staff Queue. Default scope: a doctor sees their own leftovers, staff the clinic's (same as each role's today queue).
// Windows crash-safety: Build() makes every control fresh (no view is reused with an old parent); toggles and redraws after an action run on the next
// UI turn (UiTurn.Next); the no-show confirm closes first, then the answer, toast and redraw. Every error is caught and shown in plain words.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Queue;

namespace ClinicNode.App.Views.Shared.Queue;

public sealed class LeftoverQueueSection(Page page, IServiceProvider services, Func<Guid?> doctorScope, Func<LeftoverRow, Task> onContinue, Action redraw, Func<Task> onChanged)
{
    private IReadOnlyList<LeftoverRow> _rows = [];
    private readonly Dictionary<DateOnly, bool> _toggled = new();
    private readonly HashSet<Guid> _gone = new();
    private string? _error;
    private bool _busy;

    ILeftoverQueueService Svc => (ILeftoverQueueService)services.GetService(typeof(ILeftoverQueueService))!;

    /// <summary>Reads the leftovers. Any failure (older server, offline read error) hides the section; it never breaks the page.</summary>
    public async Task LoadAsync()
    {
        try
        {
            if (services.GetService(typeof(ILeftoverQueueService)) is null) { _rows = []; return; }
            _rows = await Svc.ListAsync(doctorScope());
            _gone.RemoveWhere(id => _rows.All(r => r.BookingId != id));
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("LeftoverQueue.Load", ex);
            _rows = [];
        }
    }

    public int Count => LeftoverGroups.Group(_rows.Where(r => !_gone.Contains(r.BookingId)), ClinicClock.Today).Sum(g => g.Items.Count);

    /// <summary>A fresh section, or null when nothing is left over.</summary>
    public View? Build()
    {
        var today = ClinicClock.Today;
        var groups = LeftoverGroups.Group(_rows.Where(r => !_gone.Contains(r.BookingId)), today);
        var total = groups.Sum(g => g.Items.Count);
        if (total == 0) return null;

        var col = new VerticalStackLayout { Spacing = 0 };
        var bar = new Grid
        {
            ColumnSpacing = 8, Padding = new Thickness(14, 10), BackgroundColor = ClinicTheme.Color("StatusWarningSurface"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        bar.Add(new Label
        {
            Text = $"⚠ Not finished on an earlier day · {LeftoverGroups.PatientsLabel(total)}", FontFamily = ClinicTheme.FontSemiBold, FontSize = 14,
            TextColor = ClinicTheme.Color("StatusWarning"), VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        bar.Add(new Label { Text = "Date shown under each queue number", FontSize = 12, TextColor = ClinicTheme.Color("StatusWarning"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        col.Children.Add(bar);
        if (!string.IsNullOrWhiteSpace(_error))
        {
            var err = StaffUi.Error();
            StaffUi.Show(err, _error);
            err.Margin = new Thickness(14, 8);
            col.Children.Add(err);
        }
        foreach (var g in groups) col.Children.Add(GroupView(groups, g, today));
        return new Border
        {
            StrokeThickness = 1, Stroke = ClinicTheme.Color("StatusWarningBorder"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = 0, Content = col,
        };
    }

#if WINDOWS
    const double ActionWidth = 360; // WINDOWS ONLY: WinUI buttons are wider (padding / font); keep all three on one line without cutting
#else
    const double ActionWidth = 330;
#endif
    static readonly GridLength[] Cols = [new GridLength(92), GridLength.Star, new GridLength(110), new GridLength(150), new GridLength(ActionWidth)];

    static Grid RowGrid(Thickness pad)
    {
        var g = new Grid { ColumnSpacing = 16, Padding = pad };
        foreach (var w in Cols) g.ColumnDefinitions.Add(new ColumnDefinition(w));
        return g;
    }

    View GroupView(IReadOnlyList<LeftoverGroup> all, LeftoverGroup g, DateOnly today)
    {
        var open = LeftoverGroups.IsOpen(all, g.Date, _toggled);
        var box = new VerticalStackLayout { Spacing = 0 };
        var head = new Grid
        {
            ColumnSpacing = 8, Padding = new Thickness(14, 8), BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        var caret = ClinicTheme.Text(open ? "▾" : "▸", "LabelMd");
        caret.TextColor = ClinicTheme.Color("OnSurfaceVariant");
        var header = ClinicTheme.Text(g.Header, "LabelMd");
        header.FontAttributes = FontAttributes.Bold;
        head.Add(caret, 0, 0);
        head.Add(header, 1, 0);
        head.Add(ClinicTheme.Text(g.CountLabel, "MutedSm"), 2, 0);
        var date = g.Date;
        head.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => UiTurn.Next(() => { _toggled[date] = !open; redraw(); })) });
        SemanticProperties.SetDescription(head, $"{g.Header}, {g.CountLabel}, {(open ? "open" : "closed")}");
        box.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.6f) });
        box.Children.Add(head);
        if (!open) return box;

        var th = RowGrid(new Thickness(14, 6));
        string[] heads = ["QUEUE", "PATIENT", "VISIT", "STATUS", ""];
        for (var h = 0; h < heads.Length; h++) th.Add(ClinicTheme.Text(heads[h], "LabelSm"), h, 0);
        box.Children.Add(th);
        foreach (var r in g.Items)
        {
            box.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.6f) });
            box.Children.Add(RowView(r, g.Chip, today));
        }
        return box;
    }

    View RowView(LeftoverRow r, string chip, DateOnly today)
    {
        var row = RowGrid(new Thickness(14, 10));
        row.BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest");
        var queue = new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    StrokeThickness = 0, Padding = new Thickness(10, 5), HorizontalOptions = LayoutOptions.Start,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                    BackgroundColor = ClinicTheme.Color(r.IsPriority ? "StatusWarningSurface" : "SurfaceContainer"),
                    Content = new Label { Text = r.QueueNumber, FontFamily = ClinicTheme.FontSemiBold, FontSize = 14, TextColor = ClinicTheme.Color(r.IsPriority ? "StatusWarning" : "OnSurface") },
                },
                new Border
                {
                    StrokeThickness = 1, Stroke = ClinicTheme.Color("StatusWarningBorder"), BackgroundColor = ClinicTheme.Color("StatusWarningSurface"),
                    Padding = new Thickness(6, 1), HorizontalOptions = LayoutOptions.Start, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    Content = new Label { Text = chip, FontSize = 11, FontFamily = ClinicTheme.FontMedium, TextColor = ClinicTheme.Color("StatusWarning") },
                },
            },
        };
        var name = string.IsNullOrWhiteSpace(r.PatientName) ? "A patient" : r.PatientName;
        var who = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = name, FontFamily = ClinicTheme.FontSemiBold, FontSize = 15 } } };
        if (!string.IsNullOrWhiteSpace(r.PatientCode)) who.Children.Add(new Label { Text = r.PatientCode, FontSize = 12, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
        var visit = new Label { Text = r.VisitType == VisitType.FollowUp ? "Follow-up" : "New visit", FontSize = 14, TextColor = ClinicTheme.Color("OnSurfaceVariant"), VerticalOptions = LayoutOptions.Center };
        var (pill, since) = LeftoverGroups.StatusText(r, today);
        var inProgress = r.Status == BookingStatus.InProgress;
        var status = new VerticalStackLayout
        {
            Spacing = 3, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    StrokeThickness = 0, Padding = new Thickness(10, 4), HorizontalOptions = LayoutOptions.Start,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = ClinicTheme.Color(inProgress ? "StatusInfoSurface" : "StatusNeutralSurface"),
                    Content = new Label { Text = pill, FontFamily = ClinicTheme.FontMedium, FontSize = 12, TextColor = ClinicTheme.Color(inProgress ? "StatusInfo" : "StatusNeutral") },
                },
                new Label { Text = since, FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
            },
        };
        var act = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
        act.Children.Add(StaffUi.Btn("Continue", () => ContinueAsync(r)));
        act.Children.Add(StaffUi.Btn("Move to today", () => MoveAsync(r), "SecondaryButton"));
        var noShow = StaffUi.Link("Mark no-show", () => NoShowAsync(r));
        noShow.TextColor = ClinicTheme.Color("Error");
        noShow.VerticalOptions = LayoutOptions.Center;
        noShow.Margin = new Thickness(4, 0, 0, 0);
        act.Children.Add(noShow);
        row.Add(queue, 0, 0); row.Add(who, 1, 0); row.Add(visit, 2, 0); row.Add(status, 3, 0); row.Add(act, 4, 0);
        return row;
    }

    async Task ContinueAsync(LeftoverRow r)
    {
        if (_busy) return;
        try { _error = null; await onContinue(r); }
        catch (Exception ex) { Fail("LeftoverQueue.Continue", ex, "Couldn't open this visit. Try again."); }
    }

    async Task MoveAsync(LeftoverRow r)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _error = null;
            var res = await Svc.MoveToTodayAsync(r.BookingId);
            if (!res.Succeeded) { _error = res.Error ?? "Couldn't move this patient to today. Try again."; UiTurn.Next(redraw); return; }
            ClinicToast.Show($"{Name(r)} moved to today's queue.", ToastKind.Success);
            AfterAction(r.BookingId);
        }
        catch (Exception ex) { Fail("LeftoverQueue.Move", ex, "Couldn't move this patient to today. Check the connection and try again."); }
        finally { _busy = false; }
    }

    async Task NoShowAsync(LeftoverRow r)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _error = null;
            var id = r.BookingId;
            var done = await StaffDialog.ShowAsync(page, "Mark no-show?",
                $"{Name(r)} · {r.QueueNumber} on {LeftoverGroups.Day(r.Date, ClinicClock.Today)}\nThe earlier visit is closed as a no-show. Nothing is charged.",
                null, null, "Cancel", "Mark no-show", async _ =>
                {
                    try
                    {
                        var res = await Svc.MarkNoShowAsync(id);
                        return res.Succeeded ? null : (string.IsNullOrWhiteSpace(res.Error) ? "Couldn't mark it no-show. Try again." : res.Error);
                    }
                    catch (Exception ex)
                    {
                        ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("LeftoverQueue.NoShow", ex);
                        return Plain(ex, "Couldn't mark it no-show. Check the connection and try again.");
                    }
                }, danger: true);
            if (!done) return; // the dialog has already closed (StaffDialog: close first, then answer)
            ClinicToast.Show($"{Name(r)} marked no-show.", ToastKind.Success);
            AfterAction(id);
        }
        catch (Exception ex) { Fail("LeftoverQueue.NoShow", ex, "Couldn't mark it no-show. Try again."); }
        finally { _busy = false; }
    }

    void AfterAction(Guid id)
    {
        _gone.Add(id);
        UiTurn.Next(() =>
        {
            redraw();
            _ = SafeChangedAsync();
        });
    }

    async Task SafeChangedAsync()
    {
        try { await onChanged(); }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("LeftoverQueue.Reload", ex); }
    }

    void Fail(string source, Exception ex, string fallback)
    {
        ClinicNode.Core.Diagnostics.ClinicErrorLog.Write(source, ex);
        _error = Plain(ex, fallback);
        UiTurn.Next(redraw);
    }

    static string Plain(Exception ex, string fallback) =>
        ex is ClinicNode.Api.Http.ApiException or ClinicNode.Core.Sync.OnlineWriteFailedException or ClinicNode.Core.Sync.LocalWriteBlockedException
            && !string.IsNullOrWhiteSpace(ex.Message) ? ex.Message : fallback;

    static string Name(LeftoverRow r) => string.IsNullOrWhiteSpace(r.PatientName) ? "The patient" : r.PatientName;
}
//JJQR - END
