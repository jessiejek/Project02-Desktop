// JJQR - AI (Nov 21, 2026) - A-SET. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminSettingsViewModel : AdminViewModelBase
{
    readonly IAdminSettingsService _settings;
    readonly IAdminPatientService _patients;
    int _consentVersion;

    //JJQR - AI (21 Nov 2026) - online-only settings: connectivity + API session tell the screen whether saving is possible
    readonly ClinicNode.Api.Http.IApiConnectivity? _connectivity;
    readonly ClinicNode.Api.Http.IApiTokenStore? _tokens;

    /// <summary>False when offline or signed in offline-only: settings and fees cannot be saved (Jessie: online-only).</summary>
    public bool CanSaveOnline { get; private set; } = true;
    public const string NeedsInternetNote = "Clinic settings and fees can only be changed with an internet connection. You can look, but saving is off until this computer is back online.";

    public async Task RefreshOnlineAsync()
    {
        if (_connectivity is null) { CanSaveOnline = true; return; }
        var online = false;
        try { online = await _connectivity.EnsureCheckedAsync(); } catch { online = false; }
        CanSaveOnline = online && (_tokens is null || !string.IsNullOrEmpty(_tokens.AccessToken));
        Raise(nameof(CanSaveOnline));
    }
    //JJQR - END

    public AdminSettingsViewModel(IAdminSettingsService settings, IAdminPatientService patients,
        //JJQR - AI (21 Nov 2026)
        ClinicNode.Api.Http.IApiConnectivity? connectivity = null, ClinicNode.Api.Http.IApiTokenStore? tokens = null)
        //JJQR - END
    {
        //JJQR - AI (21 Nov 2026)
        _connectivity = connectivity;
        _tokens = tokens;
        //JJQR - END
        _settings = settings;
        _patients = patients;
        ShowTabCommand = new Command<string>(id => ShowTab(id));
        SaveGeneralCommand = new Command(async () => await SaveSettingsAsync());
        SaveHoursCommand = new Command(async () => await SaveHoursAsync());
        SavePaymentsCommand = new Command(async () => await SavePaymentsAsync());
        SaveServiceCommand = new Command(async () => await SaveServiceAsync());
        EditServiceCommand = new Command<ServiceEditRow>(BeginEditService);
        SearchMedicinesCommand = new Command(async () => await SearchMedicinesAsync());
        ResolveRequestCommand = new Command<DataRequestRow>(async row => await ResolveAsync(row));
    }

    public bool IsGeneral { get; private set; } = true;
    public bool IsHours { get; private set; }
    public bool IsFees { get; private set; }
    public bool IsPayments { get; private set; }
    public bool IsPrivacy { get; private set; }
    public bool IsBranding { get; private set; }
    public bool IsServices { get; private set; }

    public string ClinicName { get; set; } = "";
    public string Address { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Description { get; set; } = "";
    public IReadOnlyList<string> PaymentModeOptions { get; } = ["Pay at clinic", "Online"];
    public string DefaultPaymentMode { get; set; } = "Pay at clinic";
    public string RefundPolicy { get; set; } = "";
    public string FeeConsultation { get; set; } = "";
    public string FeeFollowUp { get; set; } = "";
    public string FeeSeniorPwd { get; set; } = "";
    public string FeeMedCert { get; set; } = "";
    public string DiscountPct { get; set; } = "";
    public string PrivacyPolicyText { get; set; } = "";
    public string ConsentLabel { get; private set; } = "";
    public bool BumpConsent { get; set; }
    public string PrimaryColor { get; set; } = "";
    public string SecondaryColor { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public string FaviconUrl { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string NewServiceName { get; set; } = "";
    public IReadOnlyList<string> CategoryOptions { get; } = Enum.GetNames<ServiceCategory>();
    public string NewServiceCategory { get; set; } = nameof(ServiceCategory.Consultation);
    public string NewServicePrice { get; set; } = "";
    public string NewServiceDescription { get; set; } = "";
    public bool NewServiceActive { get; set; } = true;
    Guid? _editingServiceId;
    public string ServiceFormTitle => _editingServiceId is null ? "Add a service" : "Edit service";
    public string MedicineQuery { get; set; } = "";
    public ObservableCollection<HourRow> Hours { get; } = [];
    public ObservableCollection<PaymentChoiceRow> PaymentMethods { get; } = [];
    public ObservableCollection<ServiceEditRow> Services { get; } = [];
    public ObservableCollection<string> Medicines { get; } = [];
    public ObservableCollection<DataRequestRow> DataRequests { get; } = [];
    public ICommand ShowTabCommand { get; }
    public ICommand SaveGeneralCommand { get; }
    public ICommand SaveHoursCommand { get; }
    public ICommand SavePaymentsCommand { get; }
    public ICommand SaveServiceCommand { get; }
    public ICommand EditServiceCommand { get; }
    public ICommand SearchMedicinesCommand { get; }
    public ICommand ResolveRequestCommand { get; }

    void ShowTab(string id)
    {
        IsGeneral = id == "general";
        IsHours = id == "hours";
        IsFees = id == "fees";
        IsPayments = id == "payments";
        IsPrivacy = id == "privacy";
        IsBranding = id == "branding";
        IsServices = id == "services";
        Raise(nameof(IsGeneral)); Raise(nameof(IsHours)); Raise(nameof(IsFees));
        Raise(nameof(IsPayments)); Raise(nameof(IsPrivacy)); Raise(nameof(IsBranding)); Raise(nameof(IsServices));
    }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var s = await _settings.GetAsync();
            if (s is null) { Error = "Clinic settings were not found."; return; }
            ClinicName = s.ClinicName;
            Address = s.Address;
            ContactNumber = s.ContactNumber ?? "";
            Email = s.Email ?? "";
            Description = s.Description ?? "";
            DefaultPaymentMode = s.DefaultPaymentMode == PaymentMode.Online ? "Online" : "Pay at clinic";
            RefundPolicy = s.RefundPolicy ?? "";
            FeeConsultation = s.FeeConsultation.ToString("0.##");
            FeeFollowUp = s.FeeFollowUp.ToString("0.##");
            FeeSeniorPwd = s.FeeSeniorPwd.ToString("0.##");
            FeeMedCert = s.FeeMedCert.ToString("0.##");
            DiscountPct = (s.DiscountPct * 100m).ToString("0.##");
            PrivacyPolicyText = s.PrivacyPolicyText ?? "";
            _consentVersion = s.ConsentVersion;
            ConsentLabel = $"Current consent version: {s.ConsentVersion}. Tick the box to publish version {s.ConsentVersion + 1} (can only go up by one).";
            BumpConsent = false;
            PrimaryColor = s.PrimaryColor ?? "";
            SecondaryColor = s.SecondaryColor ?? "";
            LogoUrl = s.LogoUrl ?? "";
            FaviconUrl = s.FaviconUrl ?? "";
            WebsiteUrl = s.WebsiteUrl ?? "";

            var hours = await _settings.GetOperatingHoursAsync();
            var byDay = hours.ToDictionary(h => h.DayOfWeek);
            Hours.Clear();
            for (short i = 0; i <= 6; i++)
            {
                byDay.TryGetValue(i, out var h);
                Hours.Add(new HourRow
                {
                    DayOfWeek = i,
                    DayName = DayName(i),
                    IsClosed = h?.IsClosed ?? false,
                    Open = (h?.OpenTime ?? new TimeOnly(8, 0)).ToTimeSpan(),
                    Close = (h?.CloseTime ?? new TimeOnly(17, 0)).ToTimeSpan(),
                });
            }

            var accepted = new HashSet<PaymentMethod>(await _settings.GetPaymentMethodsAsync());
            PaymentMethods.Clear();
            foreach (var method in Enum.GetValues<PaymentMethod>())
                PaymentMethods.Add(new PaymentChoiceRow { Method = method, Name = method.ToString(), IsAccepted = accepted.Contains(method) });

            //JJQR - AI (21 Nov 2026) - web /admin/settings loads settings + hours + methods only (no services); a services failure must not fail that page
            if (!_skipServices) await ReloadServicesAsync();
            //JJQR - END
            await ReloadRequestsAsync();
            RaiseSettings();
        });
    }

    void RaiseSettings()
    {
        Raise(nameof(ClinicName)); Raise(nameof(Address)); Raise(nameof(ContactNumber)); Raise(nameof(Email));
        Raise(nameof(Description)); Raise(nameof(DefaultPaymentMode)); Raise(nameof(RefundPolicy));
        Raise(nameof(FeeConsultation)); Raise(nameof(FeeFollowUp)); Raise(nameof(FeeSeniorPwd));
        Raise(nameof(FeeMedCert)); Raise(nameof(DiscountPct)); Raise(nameof(PrivacyPolicyText));
        Raise(nameof(ConsentLabel)); Raise(nameof(BumpConsent)); Raise(nameof(PrimaryColor));
        Raise(nameof(SecondaryColor)); Raise(nameof(LogoUrl)); Raise(nameof(FaviconUrl)); Raise(nameof(WebsiteUrl));
        Raise(nameof(ServiceFormTitle));
    }

    async Task ReloadServicesAsync()
    {
        Services.Clear();
        foreach (var s in await _settings.ListServicesAsync(activeOnly: false))
            Services.Add(new ServiceEditRow(s.ServiceId, s.Name, s.Category.ToString(), Peso(s.Price), s.IsActive ? "Active" : "Inactive", s.Description ?? "", s.Price, s.IsActive));
    }

    async Task ReloadRequestsAsync()
    {
        DataRequests.Clear();
        //JJQR - AI (21 Nov 2026) - raw rows for the web "Patient data requests" panel
        RequestsFailed = false;
        IReadOnlyList<DataRequestItem> all;
        try { all = await _patients.ListDataRequestsAsync(null); }
        catch { RequestsFailed = true; RawRequests = []; Raise(nameof(RawRequests)); return; }
        RawRequests = all;
        Raise(nameof(RawRequests));
        //JJQR - END
        foreach (var r in all)
        {
            DataRequests.Add(new DataRequestRow
            {
                Id = r.Id,
                Title = $"{r.PatientName}  ·  {r.PatientCode}  ·  {r.Kind}",
                Subtitle = $"{ClinicDateTime(r.RequestedAt)}  ·  {r.Status}" + (r.RetainUntil is { } until ? $"  ·  keep until {ClinicDateShort(until)}" : ""),
                Details = Dash(r.Details),
                StatusChoice = r.Status,
                Note = r.ResolutionNote ?? "",
            });
        }
    }

    ClinicSettingsEdit BuildEdit()
    {
        if (!decimal.TryParse(FeeConsultation, NumberStyles.Number, CultureInfo.CurrentCulture, out var consult)) throw new FormatException("Consultation fee must be a number.");
        if (!decimal.TryParse(FeeFollowUp, NumberStyles.Number, CultureInfo.CurrentCulture, out var follow)) throw new FormatException("Follow-up fee must be a number.");
        if (!decimal.TryParse(FeeSeniorPwd, NumberStyles.Number, CultureInfo.CurrentCulture, out var senior)) throw new FormatException("Senior/PWD fee must be a number.");
        if (!decimal.TryParse(FeeMedCert, NumberStyles.Number, CultureInfo.CurrentCulture, out var cert)) throw new FormatException("Medical certificate fee must be a number.");
        if (!decimal.TryParse(DiscountPct, NumberStyles.Number, CultureInfo.CurrentCulture, out var pct)) throw new FormatException("Discount must be a number (percent, for example 20).");
        if (pct > 1m) pct /= 100m;
        var mode = DefaultPaymentMode == "Online" ? PaymentMode.Online : PaymentMode.PayAtClinic;
        return new ClinicSettingsEdit(
            ClinicName, Address, ContactNumber, Email, Description, mode, RefundPolicy, PrivacyPolicyText,
            PrimaryColor, SecondaryColor, LogoUrl, FaviconUrl, WebsiteUrl,
            consult, follow, senior, cert, pct,
            BumpConsent ? _consentVersion + 1 : _consentVersion);
    }

    async Task SaveSettingsAsync()
    {
        await RunAsync(async () =>
        {
            ClinicSettingsEdit edit;
            try { edit = BuildEdit(); }
            catch (FormatException ex) { Error = ex.Message; return; }
            var result = await _settings.UpdateAsync(edit);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Clinic settings saved.";
            await LoadAsync();
        });
    }

    async Task SaveHoursAsync()
    {
        await RunAsync(async () =>
        {
            var rows = Hours.Select(h => new OperatingHourEdit(
                h.DayOfWeek, h.IsClosed,
                h.IsClosed ? null : TimeOnly.FromTimeSpan(h.Open),
                h.IsClosed ? null : TimeOnly.FromTimeSpan(h.Close))).ToList();
            var result = await _settings.SetOperatingHoursAsync(rows);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Operating hours saved.";
        });
    }

    async Task SavePaymentsAsync()
    {
        await RunAsync(async () =>
        {
            var methods = PaymentMethods.Where(p => p.IsAccepted).Select(p => p.Method).ToList();
            var result = await _settings.SetPaymentMethodsAsync(methods);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Accepted payment methods saved.";
        });
    }

    void BeginEditService(ServiceEditRow row)
    {
        _editingServiceId = row.ServiceId;
        NewServiceName = row.Name;
        NewServiceCategory = row.Category;
        NewServicePrice = row.PriceValue.ToString("0.##");
        NewServiceDescription = row.Description;
        NewServiceActive = row.IsActiveFlag;
        Raise(nameof(NewServiceName)); Raise(nameof(NewServiceCategory)); Raise(nameof(NewServicePrice));
        Raise(nameof(NewServiceDescription)); Raise(nameof(NewServiceActive)); Raise(nameof(ServiceFormTitle));
    }

    async Task SaveServiceAsync()
    {
        await RunAsync(async () =>
        {
            if (!Enum.TryParse<ServiceCategory>(NewServiceCategory, out var category)) { Error = "Choose a valid service category."; return; }
            if (!decimal.TryParse(NewServicePrice, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)) { Error = "Service price must be a number."; return; }
            var result = _editingServiceId is { } id
                ? await _settings.UpdateServiceAsync(id, NewServiceName, category, price, string.IsNullOrWhiteSpace(NewServiceDescription) ? null : NewServiceDescription, NewServiceActive)
                : await _settings.CreateServiceAsync(NewServiceName, category, price, string.IsNullOrWhiteSpace(NewServiceDescription) ? null : NewServiceDescription, NewServiceActive);
            if (!result.Succeeded) { Error = result.Error; return; }
            _editingServiceId = null;
            NewServiceName = NewServicePrice = NewServiceDescription = "";
            NewServiceCategory = nameof(ServiceCategory.Consultation);
            NewServiceActive = true;
            Raise(nameof(NewServiceName)); Raise(nameof(NewServicePrice)); Raise(nameof(NewServiceDescription));
            Raise(nameof(NewServiceCategory)); Raise(nameof(NewServiceActive)); Raise(nameof(ServiceFormTitle));
            StatusMessage = "Service saved.";
            await ReloadServicesAsync();
        });
    }

    async Task SearchMedicinesAsync()
    {
        await RunAsync(async () =>
        {
            var rows = await _settings.SearchMedicinesAsync(MedicineQuery);
            Medicines.Clear();
            foreach (var m in rows) Medicines.Add(m.GenericName);
            StatusMessage = rows.Count == 0 ? "No medicines match this search." : $"{rows.Count} medicine{(rows.Count == 1 ? "" : "s")}";
        });
    }

    async Task ResolveAsync(DataRequestRow row)
    {
        await RunAsync(async () =>
        {
            var result = await _patients.ResolveDataRequestAsync(row.Id, row.StatusChoice, string.IsNullOrWhiteSpace(row.Note) ? null : row.Note);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Data request updated.";
            await ReloadRequestsAsync();
        });
    }

    //JJQR - AI (21 Nov 2026) - web /admin/settings: tabs General / Hours / Fees / Payments / Privacy & consent / Branding,
    // web validation + error wording, consent bump with inline confirm, Patient data requests panel.
    public IReadOnlyList<DataRequestItem> RawRequests { get; private set; } = [];
    public bool RequestsFailed { get; private set; }
    public int ConsentVersion => _consentVersion;
    public string SaveError { get; set; } = "";
    public string RequestError { get; set; } = "";
    public bool Loaded { get; private set; }
    public bool LoadFailed { get; private set; }

    public static readonly PaymentMethod[] WebPaymentMethods = [PaymentMethod.Cash, PaymentMethod.GCash, PaymentMethod.Maya, PaymentMethod.BankTransfer];

    public bool GeneralInvalid => string.IsNullOrWhiteSpace(ClinicName) || string.IsNullOrWhiteSpace(Address);

    public bool FeesInvalid => new[] { FeeConsultation, FeeFollowUp, FeeSeniorPwd, FeeMedCert }
        .Any(f => !decimal.TryParse(f, NumberStyles.Number, CultureInfo.CurrentCulture, out var v) || v < 0);

    public HourRow? InvalidHour => Hours.FirstOrDefault(h => !h.IsClosed && h.Close <= h.Open);

    // web Promise.all(settings, hours, methods): any of them failing shows "Settings unavailable" (was: only when the name stayed empty).
    // _saved = the clinic row as last read from / written to the server, so each Save sends only its own tab group like the web:
    // saveSettings (General / Payments / Privacy / Branding) leaves fees + consent alone, saveFees sends only the fee lines,
    // and publishing consent sends only the new version — unsaved edits on other tabs are never written by the wrong button.
    bool _skipServices;
    ClinicSettingsEdit? _saved;

    public async Task LoadWebAsync()
    {
        try
        {
            Error = "";
            _skipServices = true;
            await LoadAsync();
            LoadFailed = !string.IsNullOrEmpty(Error);
        }
        catch { LoadFailed = true; }
        finally { _skipServices = false; }
        _saved = null;
        if (!LoadFailed)
        {
            try { _saved = BuildEdit() with { ConsentVersion = null, DiscountPct = WebDiscount() }; }
            catch (FormatException) { _saved = null; }
        }
        Loaded = true;
    }

    PaymentMode FormPaymentMode => DefaultPaymentMode == "Online" ? PaymentMode.Online : PaymentMode.PayAtClinic;

    /// <summary>Web saveSettings: clinic details, payment mode, refund policy, privacy policy, colours, website + accepted methods
    /// (fees and consent version unchanged). Null when saved, else the web error text.</summary>
    public async Task<string?> SaveWebAsync(string failure)
    {
        ClinicSettingsEdit edit;
        if (_saved is { } s)
            edit = s with
            {
                ClinicName = ClinicName, Address = Address, ContactNumber = ContactNumber, Email = Email, Description = Description,
                DefaultPaymentMode = FormPaymentMode, RefundPolicy = RefundPolicy, PrivacyPolicyText = PrivacyPolicyText,
                PrimaryColor = PrimaryColor, SecondaryColor = SecondaryColor, WebsiteUrl = WebsiteUrl, ConsentVersion = null,
            };
        else
        {
            try { edit = BuildEdit() with { ConsentVersion = null, DiscountPct = WebDiscount() }; }
            catch (FormatException) { return failure; }
        }
        try
        {
            var result = await _settings.UpdateAsync(edit);
            if (!result.Succeeded) return string.IsNullOrWhiteSpace(result.Error) ? failure : result.Error;
            _saved = edit;
            var methods = PaymentMethods.Where(p => p.IsAccepted).Select(p => p.Method).ToList();
            var m = await _settings.SetPaymentMethodsAsync(methods);
            if (!m.Succeeded) return string.IsNullOrWhiteSpace(m.Error) ? failure : m.Error;
        }
        catch { return failure; }
        return null;
    }

    /// <summary>Web saveFees: only the four fee lines + the discount; nothing else on the row, no payment methods.</summary>
    public async Task<string?> SaveFeesWebAsync()
    {
        const string failure = "Fees could not be saved. Try again.";
        static bool Num(string text, out decimal v) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out v);
        if (!Num(FeeConsultation, out var consult) || !Num(FeeFollowUp, out var follow) || !Num(FeeSeniorPwd, out var senior) || !Num(FeeMedCert, out var cert))
            return failure;
        ClinicSettingsEdit edit;
        if (_saved is { } s)
            edit = s with { FeeConsultation = consult, FeeFollowUp = follow, FeeSeniorPwd = senior, FeeMedCert = cert, DiscountPct = WebDiscount(), ConsentVersion = null };
        else
        {
            try { edit = BuildEdit() with { ConsentVersion = null, DiscountPct = WebDiscount() }; }
            catch (FormatException) { return failure; }
        }
        try
        {
            var result = await _settings.UpdateAsync(edit);
            if (!result.Succeeded) return string.IsNullOrWhiteSpace(result.Error) ? failure : result.Error;
            _saved = edit;
        }
        catch { return failure; }
        return null;
    }

    public async Task<string?> SaveHoursWebAsync()
    {
        const string failure = "Opening hours could not be saved. Try again.";
        try
        {
            var rows = Hours.Select(h => new OperatingHourEdit(
                h.DayOfWeek, h.IsClosed,
                h.IsClosed ? null : TimeOnly.FromTimeSpan(h.Open),
                h.IsClosed ? null : TimeOnly.FromTimeSpan(h.Close))).ToList();
            var result = await _settings.SetOperatingHoursAsync(rows);
            if (!result.Succeeded) return string.IsNullOrWhiteSpace(result.Error) ? failure : result.Error;
        }
        catch { return failure; }
        return null;
    }

    public async Task<string?> PublishConsentWebAsync()
    {
        const string failure = "The consent version could not be updated. Try again.";
        ClinicSettingsEdit edit;
        //JJQR - AI (21 Nov 2026) - web sends only consent_version: use the last saved row, not the unsaved form
        if (_saved is { } s) edit = s with { ConsentVersion = _consentVersion + 1 };
        else
        {
            try { edit = BuildEdit() with { ConsentVersion = _consentVersion + 1, DiscountPct = WebDiscount() }; }
            catch (FormatException) { return failure; }
        }
        //JJQR - END
        try
        {
            var result = await _settings.UpdateAsync(edit);
            if (!result.Succeeded) return string.IsNullOrWhiteSpace(result.Error) ? failure : result.Error;
        }
        catch { return failure; }
        _consentVersion++;
        return null;
    }

    // web: the discount box is always a whole percent (0–100), stored as a fraction
    decimal WebDiscount() =>
        decimal.TryParse(DiscountPct, NumberStyles.Number, CultureInfo.CurrentCulture, out var pct) ? Math.Clamp(pct, 0m, 100m) / 100m : 0m;

    public async Task ActOnRequestWebAsync(DataRequestItem r, string status, string? note)
    {
        RequestError = "";
        try
        {
            var result = await _patients.ResolveDataRequestAsync(r.Id, status, note);
            if (!result.Succeeded) RequestError = string.IsNullOrWhiteSpace(result.Error) ? "The request could not be updated. Try again." : result.Error;
        }
        catch { RequestError = "The request could not be updated. Try again."; }
        await ReloadRequestsAsync();
    }
    //JJQR - END

    static string DayName(short day) => day switch
    {
        0 => "Sunday", 1 => "Monday", 2 => "Tuesday", 3 => "Wednesday", 4 => "Thursday", 5 => "Friday", 6 => "Saturday",
        _ => day.ToString()
    };
}

public sealed class HourRow : AdminViewModelBase
{
    public short DayOfWeek { get; set; }
    public string DayName { get; set; } = "";
    bool _closed;
    public bool IsClosed { get => _closed; set { _closed = value; Raise(); } }
    TimeSpan _open = new(8, 0, 0);
    public TimeSpan Open { get => _open; set { _open = value; Raise(); } }
    TimeSpan _close = new(17, 0, 0);
    public TimeSpan Close { get => _close; set { _close = value; Raise(); } }
}

public sealed class PaymentChoiceRow : AdminViewModelBase
{
    public PaymentMethod Method { get; set; }
    public string Name { get; set; } = "";
    bool _accepted;
    public bool IsAccepted { get => _accepted; set { _accepted = value; Raise(); } }
}

public sealed record ServiceEditRow(Guid ServiceId, string Name, string Category, string Price, string Status, string Description, decimal PriceValue, bool IsActiveFlag);

public sealed class DataRequestRow : AdminViewModelBase
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Details { get; set; } = "";
    public IReadOnlyList<string> StatusOptions { get; } = ["Pending", "Scheduled", "Completed", "Rejected"];
    public string StatusChoice { get; set; } = "Pending";
    public string Note { get; set; } = "";
}
// JJQR - END
