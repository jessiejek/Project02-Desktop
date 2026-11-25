//JJQR - AI (2026-11-25) - why: GAV-4 (owner approved the pro mockup, "apply it also in web please"). "⚠ Not finished on an earlier day · N patients" inside
// the queue card, under today's queue and above Needs attention: one header per earlier date (most recent open, older closed), the same columns as the
// queue plus a date chip, and three actions per row: Continue (solid teal), Move to today (outline), Mark no-show (red link). Used by the doctor Today,
// doctor Appointments → Today and the staff Queue. Default scope: a doctor sees their own leftovers, staff the clinic's (same as each role's today queue).
// Windows crash-safety: Build() makes every control fresh (no view is reused with an old parent); toggles and redraws after an action run on the next
// UI turn (UiTurn.Next); the no-show confirm closes first, then the answer, toast and redraw. Every error is caught and shown in plain words.
//JJQR - AI (2026-11-26) - why: layout changed (owner t78u/t79u): its own "Earlier days" card below today's, one group per date (Queue + Needs attention) //JJQR - END
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
    private readonly HashSet<string> _listClosed = new(); //JJQR - AI (2026-11-26) - why: folded sub-lists ("<date>:queue" / "<date>:attention"), open by default //JJQR - END
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

    //JJQR - AI (2026-11-26) - why: owner (t78u) "yesterday's queue should not cross over TODAY's" + (t79u) "UNDER NOVEMBER 25 → Queue → list, → Needs
    // attention → list". Build() now returns its OWN card ("Earlier days"), which each page places BELOW its today card (never inside it). One header per
    // earlier date ("▾ Wed, Nov 25 · 2 in queue · 1 needs attention", most recent open), and under it "Queue" (that day's unfinished patients, same
    // actions) and, on the doctor Today page, "Needs attention" (that day's items, drawn by the page through attentionRows). Every control is new on each
    // call (no view reused with an old parent); header taps redraw on the next UI turn (UiTurn.Next).
    /// <summary>A fresh "Earlier days" card, or null when no earlier day has anything left.</summary>
    public View? Build(IReadOnlyList<ClinicNode.Core.Practice.AttentionGroup>? attention = null, Func<ClinicNode.Core.Practice.AttentionGroup, View>? attentionRows = null)
    {
        var today = ClinicClock.Today;
        var groups = LeftoverGroups.Group(_rows.Where(r => !_gone.Contains(r.BookingId)), today);
        var older = (attention ?? []).Where(g => g.Date < today && g.Items.Count > 0).ToList();
        var days = EarlierDays.Build(groups, older.Select(g => (g.Date, g.Items.Count)), today);
        if (days.Count == 0 && string.IsNullOrWhiteSpace(_error)) return null;

        var col = new VerticalStackLayout { Spacing = 10 };
        var title = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        title.Add(new Label { Text = "Earlier days", FontFamily = ClinicTheme.FontSemiBold, FontSize = 18, VerticalOptions = LayoutOptions.Center }, 0, 0);
        title.Add(new Label { Text = "Not finished or not cleared on their own day · separate from today", FontSize = 12, TextColor = ClinicTheme.Color("OnSurfaceVariant"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        col.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(_error))
        {
            var err = StaffUi.Error();
            StaffUi.Show(err, _error);
            col.Children.Add(err);
        }
        foreach (var d in days) col.Children.Add(DayView(days, d, today, older.FirstOrDefault(g => g.Date == d.Date), attentionRows));
        return new Border { Style = ClinicTheme.Style("Card"), Content = col };
    }

    //JJQR - AI (2026-11-26) - why: owner (pt95u) "make queue and needs attention collapsible": each sub-heading folds / unfolds its list (open by default).
    // A fresh control on every call; the tap redraws on the next UI turn (UiTurn.Next), so the tapped heading is replaced, never reused.
    public static View ListToggle(string text, bool open, Action toggle, Thickness margin)
    {
        var row = new Grid { ColumnSpacing = 8, Padding = margin, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        var caret = ClinicTheme.Text(open ? "▾" : "▸", "LabelSm");
        caret.TextColor = ClinicTheme.Color("OnSurfaceVariant");
        var l = ClinicTheme.Text(text, "LabelSm");
        l.FontAttributes = FontAttributes.Bold;
        row.Add(caret, 0, 0);
        row.Add(l, 1, 0);
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => UiTurn.Next(toggle)) });
        SemanticProperties.SetDescription(row, $"{text}, {(open ? "open" : "closed")}");
        return row;
    }

    View SubHead(string text, string key)
    {
        var open = !_listClosed.Contains(key);
        return ListToggle(text, open, () => { if (!_listClosed.Remove(key)) _listClosed.Add(key); redraw(); }, new Thickness(14, 10, 14, 4));
    }
    bool ListOpen(string key) => !_listClosed.Contains(key);
    //JJQR - END

    View DayView(IReadOnlyList<EarlierDay> all, EarlierDay d, DateOnly today, ClinicNode.Core.Practice.AttentionGroup? attention, Func<ClinicNode.Core.Practice.AttentionGroup, View>? attentionRows)
    {
        var open = EarlierDays.IsOpen(all, d.Date, _toggled);
        var box = new VerticalStackLayout { Spacing = 0 };
        var head = new Grid
        {
            ColumnSpacing = 8, Padding = new Thickness(14, 9), BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        var caret = ClinicTheme.Text(open ? "▾" : "▸", "LabelMd");
        caret.TextColor = ClinicTheme.Color("OnSurfaceVariant");
        var header = ClinicTheme.Text(d.Header, "LabelMd");
        header.FontAttributes = FontAttributes.Bold;
        head.Add(caret, 0, 0);
        head.Add(header, 1, 0);
        head.Add(ClinicTheme.Text(d.CountLabel, "MutedSm"), 2, 0);
        var date = d.Date;
        head.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => UiTurn.Next(() => { _toggled[date] = !open; redraw(); })) });
        SemanticProperties.SetDescription(head, $"{d.Header}, {d.CountLabel}, {(open ? "open" : "closed")}");
        box.Children.Add(head);
        if (open)
        {
            if (d.Queue.Count > 0)
            {
                var qKey = $"{d.Date:yyyy-MM-dd}:queue"; //JJQR - AI (2026-11-26) - why: foldable (owner pt95u) //JJQR - END
                box.Children.Add(SubHead($"Queue · {LeftoverGroups.PatientsLabel(d.Queue.Count)}", qKey));
                if (ListOpen(qKey)) { //JJQR - AI (2026-11-26) //JJQR - END
                var th = RowGrid(new Thickness(14, 6));
                th.BackgroundColor = ClinicTheme.Color("SurfaceContainerLow");
                string[] heads = ["QUEUE", "PATIENT", "VISIT", "STATUS", ""];
                for (var h = 0; h < heads.Length; h++) th.Add(ClinicTheme.Text(heads[h], "LabelSm"), h, 0);
                box.Children.Add(th);
                foreach (var r in d.Queue)
                {
                    box.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.6f) });
                    box.Children.Add(RowView(r, today));
                }
                } //JJQR - AI (2026-11-26) - end of the folded Queue list //JJQR - END
            }
            if (attention is not null && attentionRows is not null)
            {
                if (d.Queue.Count > 0) box.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.6f) });
                var aKey = $"{d.Date:yyyy-MM-dd}:attention"; //JJQR - AI (2026-11-26) - why: foldable (owner pt95u) //JJQR - END
                box.Children.Add(SubHead($"Needs attention · {ClinicNode.Core.Practice.AttentionGroups.ItemsLabel(attention.Items.Count)}", aKey));
                if (ListOpen(aKey)) box.Children.Add(attentionRows(attention)); //JJQR - AI (2026-11-26) //JJQR - END
            }
        }
        return new Border
        {
            StrokeThickness = 1, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = 0, Content = box,
        };
    }
    //JJQR - END

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

    View RowView(LeftoverRow r, DateOnly today) //JJQR - AI (2026-11-26) - why: the date is the group header now (no chip) //JJQR - END
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
