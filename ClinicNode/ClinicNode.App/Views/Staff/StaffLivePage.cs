//JJQR - AI (Nov 21, 2026) - base for staff pages: build services from DI, reload on appear + live refresh (in-process events + poll, the SignalR stand-in).
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Events;

namespace ClinicNode.App.Views.Staff;

public abstract class StaffLivePage : ContentPage
{
    protected readonly IServiceProvider Sp;
    private readonly bool _live;
    private LiveRefresh? _refresh;
    protected readonly Label LoadError = StaffUi.Error();
    //JJQR - AI (21 Nov 2026) - web <ErrorState title message onRetry/>: a page that sets LoadErrorState shows this card (title, message, "Try again")
    // instead of the one-line LoadError, and hides its body through OnLoadStateChanged. Pages that don't opt in keep the old line.
    protected readonly ContentView ErrorStateView = new() { IsVisible = false };
    protected virtual (string Title, string Message)? LoadErrorState => null;
    protected virtual void OnLoadStateChanged(bool failed) { }
    /// <summary>Web "Couldn’t refresh just now…" warning: shown (and the last data kept) when a reload fails after a successful load. Null = use the error state.</summary>
    protected virtual string? StaleLoadWarning => null;
    private bool _loadedOnce;
    //JJQR - END
    //JJQR - AI (Nov 22, 2026) - owner: "if it fetches, show a skeleton". Every page built on this base shows a pulsing placeholder over its content until the FIRST load finishes (success or failure). Later live refreshes keep the old data on screen.
    private Grid? _skeletonHost;
    private View? _skeleton;
    private bool _firstLoadDone;

    private void ShowFirstLoadSkeleton()
    {
        if (_firstLoadDone || Content is null || ReferenceEquals(Content, _skeletonHost)) return;
        var inner = Content;
        Content = null;
        var stack = new VerticalStackLayout { Spacing = 24, Padding = new Thickness(24), MaximumWidthRequest = 900, HorizontalOptions = LayoutOptions.Center };
        stack.Children.Add(new SkeletonBar(24, 240));
        stack.Children.Add(Skeleton.Card(3));
        stack.Children.Add(Skeleton.Rows(5));
        _skeleton = new Grid { BackgroundColor = ClinicTheme.Color("Background"), Children = { stack } };
        _skeletonHost = new Grid { Children = { inner, _skeleton } };
        Content = _skeletonHost;
        // never leave it stuck: after 15 s the content is shown whatever happened
        Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () => { HideFirstLoadSkeleton(); return false; });
    }

    private void HideFirstLoadSkeleton()
    {
        _firstLoadDone = true;
        if (_skeleton is not null) _skeleton.IsVisible = false;
    }
    //JJQR - END

    protected StaffLivePage(IServiceProvider sp, string title, bool live = true)
    {
        Sp = sp;
        _live = live;
        Title = title;
        BackgroundColor = ClinicTheme.Color("Background");
    }

    protected T Svc<T>() where T : notnull => StaffUi.Get<T>(Sp);

    protected abstract Task LoadAsync();

    protected async Task ReloadAsync()
    {
        try
        {
            await LoadAsync();
            HideFirstLoadSkeleton(); //JJQR - AI (Nov 22, 2026) //JJQR - END
            StaffUi.Show(LoadError, null);
            //JJQR - AI (21 Nov 2026)
            ErrorStateView.IsVisible = false;
            OnLoadStateChanged(false);
            _loadedOnce = true;
            //JJQR - END
        }
        catch (Exception ex)
        {
            HideFirstLoadSkeleton(); //JJQR - AI (Nov 22, 2026) //JJQR - END
            //JJQR - AI (21 Nov 2026) - web ErrorState when the page opts in (no exception text, like the web)
            if (_loadedOnce && StaleLoadWarning is { } stale)
            {
                StaffUi.Show(LoadError, stale); // keep what is on screen
                return;
            }
            if (LoadErrorState is { } st)
            {
                ErrorStateView.Content = StaffUi.Card(
                    ClinicTheme.Text(st.Title, "HeadlineMd"),
                    ClinicTheme.Text(st.Message, "MutedBody"),
                    StaffUi.Btn("Try again", ReloadAsync, "SecondaryButton"));
                ErrorStateView.IsVisible = true;
                OnLoadStateChanged(true);
                return;
            }
            //JJQR - END
            StaffUi.Show(LoadError, "This page didn’t load. " + ex.Message);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_live) _refresh ??= new LiveRefresh(this, Svc<IClinicEventBus>(), ReloadAsync, TimeSpan.FromSeconds(60));
        SubscribeHub(); //JJQR - AI (21 Nov 2026) //JJQR - END
        ShowFirstLoadSkeleton(); //JJQR - AI (Nov 22, 2026) //JJQR - END
        _ = ReloadAsync();
    }

    protected override void OnDisappearing()
    {
        _refresh?.Dispose();
        _refresh = null;
        UnsubscribeHub(); //JJQR - AI (21 Nov 2026) //JJQR - END
        base.OnDisappearing();
    }

    //JJQR - AI (21 Nov 2026) - live updates from /hubs/clinic (IClinicLiveEvents). Each page lists the events it cares about; the base
    // subscribes on appear, refreshes (debounced, on the UI thread) and unsubscribes on disappear. After a hub reconnect (Resync) the page
    // refetches too, because SignalR does not replay missed events. The 60 s LiveRefresh poll above stays as the fallback.
    /// <summary>Hub events that refresh this page (ClinicHubEvents.QueueUpdated / PaymentUpdated / PatientCheckedIn). Empty = none.</summary>
    protected virtual IReadOnlyList<string> HubEvents => [];

    /// <summary>Optional filter (e.g. only events for this booking). Default: every listed event refreshes.</summary>
    protected virtual bool WantsHubEvent(ClinicNode.Api.Live.ClinicHubEvent e) => true;

    // the subscribe / unsubscribe / one-reload-per-burst logic now lives in the shared Views/Shared/Live/LiveHubSubscription (doctor/admin pages reuse it)
    private ClinicNode.App.Views.Shared.Live.LiveHubSubscription? _hubLive;

    private void SubscribeHub()
    {
        if (HubEvents.Count == 0) return;
        _hubLive ??= new ClinicNode.App.Views.Shared.Live.LiveHubSubscription(Sp, HubEvents, ReloadAsync, WantsHubEvent);
        _hubLive.Attach();
    }

    private void UnsubscribeHub() => _hubLive?.Detach();
    //JJQR - END
}
//JJQR - END
