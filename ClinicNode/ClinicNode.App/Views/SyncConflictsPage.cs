// JJQR - AI (Nov 21, 2026) - sync conflict review list (GetOpenConflictsAsync / ResolveConflictAsync).
//JJQR - AI (21 Nov 2026) - item 6: finished review page: plain-language kind, "this computer vs clinic server" field diff from LocalJson/CloudJson,
// Sync now / Refresh, live reload when the sync status changes, mark reviewed one by one or all. The server version is what the clinic keeps
// (the cloud already applied its rule); "Mark reviewed" just closes the item. Values are shown on screen only, never logged.
using System.Globalization;
using System.Text.Json.Nodes;
using ClinicNode.App.Controls;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;

namespace ClinicNode.App.Views;

public sealed class SyncConflictsPage : ContentPage
{
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);
    private readonly ISyncStatus _sync;
    private readonly VerticalStackLayout _list = new() { Spacing = 12 };
    private readonly Label _empty = new() { Style = ClinicTheme.Style("MutedBody") };
    private readonly Label _error = new() { Style = ClinicTheme.Style("ErrorText"), IsVisible = false };
    private readonly Label _status = new() { Style = ClinicTheme.Style("MutedSm") };
    private readonly Button _resolveAll = new() { Text = "Mark all reviewed", Style = ClinicTheme.Style("SecondaryButton"), IsVisible = false };
    private IReadOnlyList<SyncConflict> _rows = [];
    private bool _loading;

    public SyncConflictsPage(ISyncStatus sync)
    {
        _sync = sync;
        Title = "Sync review";
        BackgroundColor = ClinicTheme.Color("Background");
        var refresh = new Button { Text = "Refresh", Style = ClinicTheme.Style("SecondaryButton") };
        refresh.Clicked += async (_, _) => await refresh.RunBusyAsync(ReloadAsync);
        var syncNow = new Button { Text = "Sync now" };
        syncNow.Clicked += (_, _) => { _sync.RequestSync(); _status.Text = "Syncing..."; };
        _resolveAll.Clicked += async (_, _) => await _resolveAll.RunBusyAsync(ResolveAllAsync);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                MaximumWidthRequest = 880,
                Children =
                {
                    ClinicTheme.Text("Sync review", "HeadlineMd"),
                    ClinicTheme.Text("Changes from this computer that the clinic server merged, changed or refused while it was offline. Check each one, fix anything on the record itself if needed, then mark it reviewed.", "MutedBody"),
                    new HorizontalStackLayout { Spacing = 8, Children = { syncNow, refresh, _resolveAll } },
                    _status,
                    _error,
                    _empty,
                    _list,
                }
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _sync.PropertyChanged += OnSyncChanged;
        _ = ReloadAsync();
    }

    protected override void OnDisappearing()
    {
        _sync.PropertyChanged -= OnSyncChanged;
        base.OnDisappearing();
    }

    void OnSyncChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISyncStatus.ConflictCount) or nameof(ISyncStatus.Phase) or null)
            MainThread.BeginInvokeOnMainThread(() => _ = ReloadAsync());
    }

    async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        _error.IsVisible = false;
        try
        {
            _status.Text = _sync.Banner;
            _rows = await _sync.GetOpenConflictsAsync();
            _list.Children.Clear();
            _resolveAll.IsVisible = _rows.Count > 1;
            if (_rows.Count == 0)
            {
                _empty.Text = "No open sync items to review.";
                _empty.IsVisible = true;
                return;
            }
            _empty.IsVisible = false;
            foreach (var row in _rows)
                _list.Children.Add(BuildCard(row));
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message;
            _error.IsVisible = true;
        }
        finally { _loading = false; }
    }

    async Task ResolveAllAsync()
    {
        if (!await DisplayAlertAsync("Mark all reviewed", $"Close all {_rows.Count} items? The clinic server's version stays as it is.", "Mark reviewed", "Cancel")) return;
        try
        {
            foreach (var row in _rows) await _sync.ResolveConflictAsync(row.Id);
        }
        catch (Exception ex) { _error.Text = ex.Message; _error.IsVisible = true; }
        await ReloadAsync();
    }

    static (string Label, string Pill) KindInfo(string kind) => kind switch
    {
        "merged" => ("Merged with a change made elsewhere", "PillInfo"),
        "conflict" => ("Edited in two places", "PillWarning"),
        "rejected" => ("Refused by the clinic server", "PillDanger"),
        "cloud_edit_flagged" => ("Changed on the clinic server meanwhile", "PillWarning"),
        "patient_code_replaced" => ("Patient code replaced: reprint the card", "PillInfo"),
        "illegal_transition" => ("Status change not allowed any more", "PillDanger"),
        "apply_failed" => ("Could not apply a server change here", "PillDanger"),
        "finished_immutable" => ("Visit already finished: change not applied", "PillWarning"),
        //JJQR - AI (21 Nov 2026) - E: OR clash (accepted, flagged for admin) and changes skipped on this computer; queue number reassigned by the cloud
        "or_number_clash" => ("OR number clash: flagged for admin", "PillWarning"),
        "skipped" => ("Skipped on this computer", "PillNeutral"),
        "queue_number_clash" => ("Queue number clash: flagged for admin", "PillWarning"),
        //JJQR - AI (21 Nov 2026) - TASK 7: queue_number_reassigned is NOT a clash: the booking had no number and the clinic server assigned one
        "queue_number_reassigned" => ("Queue number assigned by the clinic server", "PillInfo"),
        "online_only" => ("Online only: not sent from this computer", "PillWarning"),
        "reaction_notes_too_long" => ("Vaccination not sent: reaction notes too long", "PillWarning"),
        "vaccination_rejected" => ("Vaccination not sent: refused by the clinic server", "PillDanger"),
        "vaccination_session_expired" => ("Vaccination not sent: sign in again", "PillWarning"),
        "vaccination_forbidden" => ("Vaccination not sent: not allowed for this account", "PillDanger"),
        "vaccination_not_found" => ("Vaccination not sent: patient not found on the clinic server", "PillWarning"),
        "vaccination_conflict" => ("Vaccination not sent: conflict on the clinic server", "PillWarning"),
        //JJQR - END
        //JJQR - END
        _ => (string.IsNullOrWhiteSpace(kind) ? "Item" : kind, "PillNeutral"),
    };

    //JJQR - AI (21 Nov 2026) - TASK 7
    static readonly HashSet<string> SpecificSkipReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "online_only", "reaction_notes_too_long", "vaccination_rejected", "vaccination_session_expired", "vaccination_forbidden", "vaccination_not_found", "vaccination_conflict",
    };
    //JJQR - END

    static string Entity(string aggregate) => aggregate switch
    {
        "booking" or "bookings" => "Visit / booking",
        "patient" or "patients" => "Patient",
        "payment" or "payments" => "Payment",
        "consultation" or "consultations" => "Consultation",
        _ => string.IsNullOrWhiteSpace(aggregate) ? "Record" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(aggregate.Replace('_', ' ')),
    };

    View BuildCard(SyncConflict row)
    {
        var when = row.CreatedAt.ToOffset(Manila).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (Asia/Manila)";
        //JJQR - AI (21 Nov 2026) - TASK 7: a "skipped" item with a known reason code gets its own label instead of the generic skipped pill
        var (label, pill) = KindInfo(row.Kind == "skipped" && row.ReasonCode is { } rc && SpecificSkipReasons.Contains(rc) ? rc : row.Kind);
        //JJQR - END
        var pillView = new Border
        {
            Style = ClinicTheme.Style(pill) ?? ClinicTheme.Style("PillNeutral"),
            Content = ClinicTheme.Text(label, pill.Replace("Pill", "PillLabel")),
            HorizontalOptions = LayoutOptions.Start,
        };
        var meta = ClinicTheme.Text($"{Entity(row.Aggregate)}  ·  {when}" + (string.IsNullOrWhiteSpace(row.ReasonCode) ? "" : $"  ·  {row.ReasonCode}"), "MutedSm");
        var body = new VerticalStackLayout { Spacing = 8, Children = { pillView, meta } };
        if (!string.IsNullOrWhiteSpace(row.Message)) body.Children.Add(ClinicTheme.Text(row.Message!, "BodyMd"));

        var diff = BuildDiff(row.LocalJson, row.CloudJson);
        if (diff is not null) body.Children.Add(diff);

        var resolve = new Button { Text = "Mark reviewed", Style = ClinicTheme.Style("SecondaryButton"), HorizontalOptions = LayoutOptions.Start };
        resolve.Clicked += async (_, _) =>
        {
            resolve.IsEnabled = false;
            try
            {
                await _sync.ResolveConflictAsync(row.Id);
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                resolve.IsEnabled = true;
                _error.Text = ex.Message;
                _error.IsVisible = true;
            }
        };
        body.Children.Add(resolve);
        return new Border { Style = ClinicTheme.Style("Card"), Content = body };
    }

    /// <summary>Field | This computer | Clinic server, only for top-level fields that differ (max 12 rows).</summary>
    static View? BuildDiff(string? localJson, string? cloudJson)
    {
        JsonObject? local = Parse(localJson), cloud = Parse(cloudJson);
        if (local is null && cloud is null) return null;
        var keys = (local?.Select(p => p.Key) ?? []).Union(cloud?.Select(p => p.Key) ?? [])
            .Where(k => k is not ("row_version" or "updated_at" or "created_at" or "origin_node_id"))
            .Where(k => !string.Equals(local?[k]?.ToJsonString(), cloud?[k]?.ToJsonString(), StringComparison.Ordinal))
            .Take(12).ToList();
        if (keys.Count == 0) return null;

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)), new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)) },
            ColumnSpacing = 12,
            RowSpacing = 6,
        };
        void Row(int r, string a, string b, string c, string style)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(ClinicTheme.Text(a, style), 0, r);
            grid.Add(ClinicTheme.Text(b, style), 1, r);
            grid.Add(ClinicTheme.Text(c, style), 2, r);
        }
        Row(0, "Field", "This computer", "Clinic server", "TableHeader");
        for (var i = 0; i < keys.Count; i++)
            Row(i + 1, keys[i].Replace('_', ' '), Show(local?[keys[i]]), Show(cloud?[keys[i]]), "BodySm");
        return new Border { Style = ClinicTheme.Style("TableFrame"), Padding = 12, Content = grid };
    }

    static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; } catch { return null; }
    }

    static string Show(JsonNode? v) => v switch
    {
        null => "—",
        JsonValue val when val.TryGetValue<string>(out var s) => string.IsNullOrEmpty(s) ? "—" : s,
        _ => v.ToJsonString(),
    };
}
//JJQR - END
// JJQR - END
