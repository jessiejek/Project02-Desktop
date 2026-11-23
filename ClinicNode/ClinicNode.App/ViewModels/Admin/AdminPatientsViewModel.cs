// JJQR - AI (Nov 21, 2026) - A-PAT-LIST / A-PAT-DETAIL. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminPatientsViewModel : AdminViewModelBase
{
    readonly IAdminPatientService _patients;

    public AdminPatientsViewModel(IAdminPatientService patients)
    {
        _patients = patients;
        SearchCommand = new Command(async () => { _page = 1; await LoadAsync(); });
        NextPageCommand = new Command(async () => { if (CanNext) { _page++; await LoadAsync(); } }, () => CanNext);
        PrevPageCommand = new Command(async () => { if (CanPrev) { _page--; await LoadAsync(); } }, () => CanPrev);
        OpenAddCommand = new Command(() => { AddOpen = true; Raise(nameof(AddOpen)); });
        CancelAddCommand = new Command(() => { AddOpen = false; Raise(nameof(AddOpen)); });
        SaveAddCommand = new Command(async () => await SaveAddAsync());
        OpenPatientCommand = new Command<Guid>(async id => await GoAsync($"admin-patient-detail?id={id}"));
    }

    public string Query { get => _query; set { _query = value; Raise(); } }
    string _query = "";
    public bool AddOpen { get; private set; }
    public string NewFirstName { get; set; } = "";
    public string NewMiddleName { get; set; } = "";
    public string NewLastName { get; set; } = "";
    public DateTime NewDob { get; set; } = DateTime.Today.AddYears(-30);
    public string NewSex { get; set; } = "Female";
    public string NewContact { get; set; } = "";
    public string NewEmail { get; set; } = "";
    public string NewAddress { get; set; } = "";
    public IReadOnlyList<string> SexOptions { get; } = ["Female", "Male"];
    public ObservableCollection<PatientRow> Items { get; } = [];
    public string PageLabel { get; private set; } = "";
    public bool CanPrev { get; private set; }
    public bool CanNext { get; private set; }
    int _page = 1;
    const int PageSize = 25;
    public ICommand SearchCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand OpenAddCommand { get; }
    public ICommand CancelAddCommand { get; }
    public ICommand SaveAddCommand { get; }
    public ICommand OpenPatientCommand { get; }

    //JJQR - AI (21 Nov 2026) - web /admin/patients load: a newer search/page supersedes an in-flight one (web cancels the stale effect);
    // RunAsync's busy guard used to drop the newer request. Failure keeps the old rows but the page shows ErrorState "Patients unavailable".
    int _loadSeq;
    public bool IsLoading { get; private set; }
    public bool LoadFailed { get; private set; }

    public async Task LoadAsync()
    {
        var seq = ++_loadSeq;
        IsLoading = true; Raise(nameof(IsLoading));
        AdminPage<AdminPatientListItem> page;
        try
        {
            page = await _patients.SearchAsync(string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(), page: _page, pageSize: PageSize, sort: "-created");
        }
        catch
        {
            if (seq != _loadSeq) return;
            LoadFailed = true; IsLoading = false;
            Raise(nameof(LoadFailed)); Raise(nameof(IsLoading));
            return;
        }
        if (seq != _loadSeq) return;
        LoadFailed = false;
        Total = page.TotalCount;
        PageCount = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)PageSize));
        Items.Clear();
        foreach (var p in page.Items)
            Items.Add(ToRow(p));
        CanPrev = _page > 1;
        CanNext = _page < PageCount;
        IsLoading = false;
        Raise(nameof(LoadFailed)); Raise(nameof(IsLoading)); Raise(nameof(CanPrev)); Raise(nameof(CanNext));
        (NextPageCommand as Command)?.ChangeCanExecute();
        (PrevPageCommand as Command)?.ChangeCanExecute();
    }

    static PatientRow ToRow(AdminPatientListItem p) => new(
        p.PatientId,
        $"{p.FirstName} {p.LastName}",
        p.PatientCode,
        Dash(p.ContactNumber),
        Dash(string.IsNullOrWhiteSpace(p.Email) ? null : p.Email),
        p.IsGuest ? "Guest" : "Account",
        AdminWebRules.AccountStatus(p.UserId, p.IsGuest));
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - web /admin/patients: debounced search box, "a–b of N" + Previous / "Page x of y" / Next, "Add a patient" modal
    // (First name, Last name, Date of birth, Sex, Contact number (optional)) with the web's warnings and error text.
    public int Total { get; private set; }
    public int PageCount { get; private set; } = 1;
    public int CurrentPage => _page;
    public string RangeLabel => IsLoading ? "Loading patients…" : Total == 0 ? "0–0 of 0" : $"{(_page - 1) * PageSize + 1}–{Math.Min(_page * PageSize, Total)} of {Total}";
    public string EmptyMessage => string.IsNullOrWhiteSpace(Query) ? "No patients registered yet." : $"No patients match \u201c{Query.Trim()}\u201d.";
    public DateTime? NewDobPicked { get; set; }
    public string? NewSexPicked { get; set; }
    public string CreateError { get; private set; } = "";
    public bool NewPrivacyNoticeShown { get; set; } //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH): the front desk showed the notice //JJQR - END

    public async Task GoToPageAsync(int page)
    {
        _page = Math.Clamp(page, 1, PageCount);
        await LoadAsync();
    }

    public async Task SearchFromBoxAsync(string text)
    {
        Query = text ?? "";
        _page = 1;
        await LoadAsync();
    }

    public string? NewPatientProblem =>
        AdminWebRules.FutureDateProblem(NewDobPicked is { } d ? DateOnly.FromDateTime(d) : null)
        ?? AdminWebRules.LengthProblem(NewFirstName, 100, "First name")
        ?? AdminWebRules.LengthProblem(NewLastName, 100, "Last name")
        ?? AdminWebRules.PhoneProblem(NewContact);

    public bool CanCreate => NewPatientProblem is null && !string.IsNullOrWhiteSpace(NewFirstName) && !string.IsNullOrWhiteSpace(NewLastName)
        && NewDobPicked is not null && !string.IsNullOrEmpty(NewSexPicked) && NewPrivacyNoticeShown; //JJQR - AI (Nov 23, 2026) - the privacy notice must be shown first //JJQR - END

    public void ResetNewPatient()
    {
        NewFirstName = NewLastName = NewContact = "";
        NewDobPicked = null; NewSexPicked = null; CreateError = ""; NewPrivacyNoticeShown = false;
    }

    /// <summary>True when the patient was added (the modal closes); false keeps the modal and typed details.</summary>
    public async Task<bool> CreateWebAsync()
    {
        if (!CanCreate) return false;
        CreateError = "";
        try
        {
            var sex = NewSexPicked == "Male" ? SexType.Male : SexType.Female;
            var result = await _patients.CreateAsync(new PatientEdit(NewFirstName, NewLastName, DateOnly.FromDateTime(NewDobPicked!.Value), sex,
                ContactNumber: string.IsNullOrWhiteSpace(NewContact) ? null : NewContact, PrivacyNoticeShown: NewPrivacyNoticeShown));
            if (!result.Succeeded) { CreateError = string.IsNullOrWhiteSpace(result.Error) ? "The patient could not be added. Check the details and try again." : result.Error; return false; }
            // web: the new row goes on top of the current page and the total goes up by one (no reload, page and search kept)
            if (result.Value is { } added) { Items.Insert(0, ToRow(added)); Total++; PageCount = Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize)); }
        }
        // web friendlyError: network / 500 → the fallback sentence, not the raw exception text
        catch { CreateError = "The patient could not be added. Check the details and try again."; return false; }
        ResetNewPatient();
        return true;
    }
    //JJQR - END

    async Task SaveAddAsync()
    {
        await RunAsync(async () =>
        {
            if (!Enum.TryParse<SexType>(NewSex, out var sex)) sex = SexType.Female;
            var result = await _patients.CreateAsync(new PatientEdit(
                NewFirstName, NewLastName, DateOnly.FromDateTime(NewDob), sex,
                MiddleName: string.IsNullOrWhiteSpace(NewMiddleName) ? null : NewMiddleName,
                ContactNumber: NewContact,
                Email: string.IsNullOrWhiteSpace(NewEmail) ? null : NewEmail,
                Address: string.IsNullOrWhiteSpace(NewAddress) ? null : NewAddress));
            if (!result.Succeeded) { Error = result.Error; return; }
            AddOpen = false;
            Raise(nameof(AddOpen));
            NewFirstName = NewMiddleName = NewLastName = NewContact = NewEmail = NewAddress = "";
            Raise(nameof(NewFirstName)); Raise(nameof(NewMiddleName)); Raise(nameof(NewLastName));
            Raise(nameof(NewContact)); Raise(nameof(NewEmail)); Raise(nameof(NewAddress));
            await LoadAsync();
        });
    }
}

public sealed class AdminPatientDetailViewModel : AdminViewModelBase, IQueryAttributable
{
    readonly IAdminPatientService _patients;
    Guid _id;

    public AdminPatientDetailViewModel(IAdminPatientService patients)
    {
        _patients = patients;
        SaveEditCommand = new Command(async () => await SaveEditAsync());
        ToggleEditCommand = new Command(() => { EditOpen = !EditOpen; Raise(nameof(EditOpen)); });
        OpenBookingCommand = new Command<Guid>(async id => await GoAsync($"admin-booking-detail?id={id}"));
        ResolveRequestCommand = new Command<DataRequestRow>(async row => await ResolveAsync(row));
    }

    public string Name { get; private set; } = "Patient";
    public string Code { get; private set; } = "";
    public string Overview { get; private set; } = "";
    public bool EditOpen { get; private set; }
    public string EditFirstName { get; set; } = "";
    public string EditMiddleName { get; set; } = "";
    public string EditLastName { get; set; } = "";
    public DateTime EditDob { get; set; } = DateTime.Today;
    public string EditSex { get; set; } = "Female";
    public string EditContact { get; set; } = "";
    public string EditEmail { get; set; } = "";
    public string EditAddress { get; set; } = "";
    public IReadOnlyList<string> SexOptions { get; } = ["Female", "Male"];
    public ObservableCollection<PatientBookingRow> Bookings { get; } = [];
    public ObservableCollection<PatientConsultRow> Consultations { get; } = [];
    public ObservableCollection<AccessRow> AccessLog { get; } = [];
    public ObservableCollection<DataRequestRow> DataRequests { get; } = [];
    public ICommand SaveEditCommand { get; }
    public ICommand ToggleEditCommand { get; }
    public ICommand OpenBookingCommand { get; }
    public ICommand ResolveRequestCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var raw) && Guid.TryParse(raw?.ToString(), out var id))
            _id = id;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_id == Guid.Empty) return;
        await RunAsync(async () =>
        {
            var result = await _patients.GetAsync(_id);
            if (!result.Succeeded || result.Value is null) { Error = result.Error ?? "Patient unavailable"; return; }
            var d = result.Value;
            var p = d.Patient;
            //JJQR - AI (21 Nov 2026)
            Detail = d;
            Raise(nameof(Detail));
            //JJQR - END
            Name = $"{p.FirstName} {p.LastName}".Trim();
            Code = p.PatientCode;
            Overview = $"Sex  {p.Sex}\nDate of birth  {ClinicDate(p.DateOfBirth)}\nContact number  {Dash(p.ContactNumber)}\nEmail  {Dash(string.IsNullOrWhiteSpace(p.Email) ? null : p.Email)}\nAddress  {Dash(d.Address)}";
            EditFirstName = p.FirstName; EditLastName = p.LastName;
            EditDob = p.DateOfBirth.ToDateTime(TimeOnly.MinValue);
            EditSex = p.Sex.ToString();
            EditContact = p.ContactNumber ?? ""; EditEmail = p.Email ?? "";
            EditMiddleName = p.MiddleName ?? "";
            EditAddress = d.Address ?? "";
            Bookings.Clear();
            foreach (var b in d.Bookings)
                Bookings.Add(new PatientBookingRow(b.BookingId, $"{ClinicDateShort(b.AppointmentDate)}  ·  {Dash(b.DoctorName)}", StatusText(b.Status)));
            Consultations.Clear();
            foreach (var c in d.Consultations)
                Consultations.Add(new PatientConsultRow($"{ClinicDateShort(c.Date)}  ·  {Dash(c.DoctorName)}", StatusText(c.Status)));
            AccessLog.Clear();
            foreach (var a in d.AccessLog)
                AccessLog.Add(new AccessRow(ClinicDateTime(a.At), a.Who, string.IsNullOrWhiteSpace(a.Role) ? a.Area : $"{a.Role}  ·  {a.Area}"));
            Raise(nameof(Name)); Raise(nameof(Code)); Raise(nameof(Overview));
            Raise(nameof(EditFirstName)); Raise(nameof(EditLastName)); Raise(nameof(EditDob));
            Raise(nameof(EditSex)); Raise(nameof(EditContact)); Raise(nameof(EditEmail)); Raise(nameof(EditMiddleName)); Raise(nameof(EditAddress));

            DataRequests.Clear();
            foreach (var r in (await _patients.ListDataRequestsAsync(null)).Where(x => x.PatientId == _id))
            {
                DataRequests.Add(new DataRequestRow
                {
                    Id = r.Id,
                    Title = $"{r.Kind}  ·  {r.Status}",
                    Subtitle = ClinicDateTime(r.RequestedAt) + (r.RetainUntil is { } until ? $"  ·  keep until {ClinicDateShort(until)}" : ""),
                    Details = Dash(r.Details),
                    StatusChoice = r.Status,
                    Note = r.ResolutionNote ?? "",
                });
            }
        });
    }

    async Task SaveEditAsync()
    {
        await RunAsync(async () =>
        {
            if (!Enum.TryParse<SexType>(EditSex, out var sex)) sex = SexType.Female;
            var result = await _patients.UpdateAsync(_id, new PatientEdit(
                EditFirstName, EditLastName, DateOnly.FromDateTime(EditDob), sex,
                MiddleName: string.IsNullOrWhiteSpace(EditMiddleName) ? null : EditMiddleName,
                ContactNumber: EditContact, Email: EditEmail,
                Address: string.IsNullOrWhiteSpace(EditAddress) ? null : EditAddress));
            if (!result.Succeeded) { Error = result.Error; return; }
            EditOpen = false; Raise(nameof(EditOpen));
            await LoadAsync();
        });
    }

    //JJQR - AI (21 Nov 2026) - web /admin/patients/[id]: header, tabs Overview / Bookings (n) / Consultations (n) / Access log, "Edit patient details" modal.
    public AdminPatientDetail? Detail { get; private set; }

    static string TitleCase(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[^\s'-]+", m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..].ToLowerInvariant());

    /// <summary>Returns null when saved, else the web's error text. Middle name and address (not on the web form) are kept.</summary>
    public async Task<string?> SaveWebAsync(string firstName, string lastName, DateTime? dob, string sex, string contact, string email)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || dob is null)
            return "First name, last name, and date of birth are required.";
        try
        {
            var current = Detail?.Patient;
            var result = await _patients.UpdateAsync(_id, new PatientEdit(
                TitleCase(firstName.Trim()), TitleCase(lastName.Trim()), DateOnly.FromDateTime(dob.Value), sex == "Female" ? SexType.Female : SexType.Male,
                MiddleName: current?.MiddleName,
                ContactNumber: string.IsNullOrWhiteSpace(contact) ? null : contact.Trim(),
                Email: email.Trim(),
                Address: Detail?.Address));
            if (!result.Succeeded) return "Could not save patient. Please try again.";
        }
        catch { return "Could not save patient. Please try again."; }
        await LoadAsync();
        return null;
    }
    //JJQR - END

    async Task ResolveAsync(DataRequestRow row)
    {
        await RunAsync(async () =>
        {
            var result = await _patients.ResolveDataRequestAsync(row.Id, row.StatusChoice, string.IsNullOrWhiteSpace(row.Note) ? null : row.Note);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Data request updated.";
            await LoadAsync();
        });
    }
}

public sealed record PatientRow(Guid PatientId, string Name, string Code, string Contact, string Email, string Account,
    //JJQR - AI (21 Nov 2026) - LinkedAccount / NoAccount / AccountUnknown for the StatusPill
    string AccountKey = "");
    //JJQR - END
public sealed record PatientBookingRow(Guid BookingId, string Subtitle, string Status);
public sealed record PatientConsultRow(string Subtitle, string Status);
public sealed record AccessRow(string When, string Who, string Area);
// JJQR - END
