//JJQR - AI (Nov 21, 2026) - list state for the doctor appointments screen (D-APPTS): scope toggle, status filter, search, 20-per-page paging.
//JJQR - AI (21 Nov 2026) - 1:1 pass (web doctor/appointments/page.tsx): a snapshot per scope (Loaded = web `current`), Fetching / Error, a scope
// change clears the rows, search and page; "Clear filters" goes to All statuses (web setFilter("all")).
//JJQR - END
using ClinicApp.Domain.Enums;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.ViewModels.Doctor.Extra;

public sealed class DoctorAppointmentsViewModel
{
    public AppointmentScope Scope { get; set; } = AppointmentScope.Today;
    public AppointmentFilter Filter { get; set; } = AppointmentFilter.Active;
    public string Search { get; set; } = "";
    public int Page { get; set; } = 1;
    public IReadOnlyList<AppointmentRow> All { get; private set; } = [];
    public DateTimeOffset? UpdatedAt { get; private set; }
    //JJQR - AI (21 Nov 2026)
    /// <summary>Rows for the current scope have loaded at least once (web <c>current</c>).</summary>
    public bool Loaded { get; private set; }
    public bool Fetching { get; set; } = true;
    public string? Error { get; set; }
    /// <summary>web <c>loading</c>: fetching and nothing for this scope yet.</summary>
    public bool Loading => Fetching && !Loaded;
    public const string RefreshFailed = "Appointments could not be refreshed. Please try again.";
    //JJQR - END

    public string Title => Scope == AppointmentScope.Today ? "Today's appointments" : "Appointment history";
    public string ListTitle => Scope == AppointmentScope.Today ? "Patient queue" : "All appointments";

    public void SetRows(IReadOnlyList<AppointmentRow> rows)
    {
        All = DoctorExtraRules.Order(rows, Scope);
        UpdatedAt = DateTimeOffset.Now;
        Loaded = true; //JJQR - AI (21 Nov 2026) //JJQR - END
        if (Page > PageCount) Page = PageCount;
    }

    public void SetScope(AppointmentScope scope)
    {
        //JJQR - AI (21 Nov 2026) - web changeScope: no-op for the same scope; new scope starts empty and loading
        if (Scope == scope) return;
        Scope = scope;
        Filter = scope == AppointmentScope.Today ? AppointmentFilter.Active : AppointmentFilter.All;
        Page = 1;
        Search = "";
        Error = null;
        Fetching = true;
        Loaded = false;
        All = [];
        UpdatedAt = null;
        //JJQR - END
    }

    public IReadOnlyList<AppointmentRow> Filtered => DoctorExtraRules.Filter(All, Filter, Search);
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Filtered.Count / (double)DoctorExtraRules.AppointmentPageSize));
    //JJQR - AI (21 Nov 2026) - web currentPage = min(page, pages)
    public int CurrentPage => Math.Min(Math.Max(1, Page), PageCount);
    public IReadOnlyList<AppointmentRow> Visible => Filtered.Skip((CurrentPage - 1) * DoctorExtraRules.AppointmentPageSize).Take(DoctorExtraRules.AppointmentPageSize).ToList();
    /// <summary>web: "Clear filters" shows when there are rows and a search or any status other than All statuses.</summary>
    public bool HasFilters => All.Count > 0 && (!string.IsNullOrWhiteSpace(Search) || Filter != AppointmentFilter.All);
    //JJQR - END

    public int Count(BookingStatus s) => All.Count(r => r.Status == s);
    public AppointmentRow? Focus => Scope == AppointmentScope.Today ? DoctorExtraRules.Focus(All) : null;

    public void ClearFilters()
    {
        Search = "";
        Filter = AppointmentFilter.All; //JJQR - AI (21 Nov 2026) web setFilter("all") //JJQR - END
        Page = 1;
    }
}
//JJQR - END
