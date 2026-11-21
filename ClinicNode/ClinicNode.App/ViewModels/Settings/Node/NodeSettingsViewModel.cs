//JJQR - AI (Nov 21, 2026) - "This computer" settings: sync URL, register node, sync now, printers/paper per document, backup & restore, encryption status.
// Everything the page does is a method/command here so it is unit-testable; dialogs and the UI-thread hop are injected.
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClinicNode.App.Services.Backup;
using ClinicNode.Core.NodeSettings;
using ClinicNode.Core.Printing;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Wire;

namespace ClinicNode.App.ViewModels.Settings.Node;

/// <summary>Questions the VM needs the user to answer (the page implements these with DisplayAlert / DisplayPromptAsync).</summary>
public interface INodeSettingsDialogs
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    /// <summary>Password prompt; null = cancelled.</summary>
    Task<string?> PromptPasswordAsync(string title, string message);
    /// <summary>The app cannot relaunch itself (Mac sandbox): tell the user, then the VM quits.</summary>
    Task InformAsync(string title, string message);
}

public sealed class DocumentPrinterRow : INotifyPropertyChanged
{
    public ClinicPrintKind Kind { get; init; }
    public string Label { get; init; } = "";
    string? _printer;
    PrinterPaper _paper;
    int _dialogChoice;
    public string? PrinterName { get => _printer; set { _printer = value; Raise(); } }
    public PrinterPaper Paper { get => _paper; set { _paper = value; Raise(); } }
    /// <summary>0 = follow the global setting, 1 = ask (system print dialog), 2 = print straight to the named printer.</summary>
    public int DialogChoice { get => _dialogChoice; set { _dialogChoice = value; Raise(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class NodeSettingsViewModel : INotifyPropertyChanged
{
    public static readonly string[] DialogChoices = ["Default (global setting)", "Ask me each time", "Print straight to the printer"];
    public static readonly IReadOnlyList<PrinterPaper> Papers = Enum.GetValues<PrinterPaper>();
    public static readonly IReadOnlyList<QueueSlipStyle> SlipStyles = Enum.GetValues<QueueSlipStyle>();
    public static readonly IReadOnlyList<ThermalTransport> Transports = Enum.GetValues<ThermalTransport>();

    readonly ISyncEndpoint _endpoint;
    readonly ISyncNodeRegistration _registration;
    readonly ISyncService _sync;
    readonly PrintingOptions _printing;
    readonly NodeOptions _node;
    readonly NodeBackupCoordinator? _backup;
    readonly DatabaseEncryptionResult _encryption;
    readonly INodeSettingsDialogs _dialogs;
    readonly Action<Action> _ui;
    readonly Action _quit;
    readonly string _version;
    readonly string _machine;
    readonly TimeProvider _clock;
    readonly TimeSpan _pollInterval;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    CancellationTokenSource? _connectionWatch;

    public NodeSettingsViewModel(ISyncEndpoint endpoint, ISyncNodeRegistration registration, ISyncService sync, PrintingOptions printing,
        NodeOptions node, DatabaseEncryptionResult encryption, INodeSettingsDialogs dialogs, NodeBackupCoordinator? backup = null,
        Action<Action>? ui = null, Action? quit = null, string appVersion = "", string machineName = "",
        TimeProvider? clock = null, TimeSpan? connectionPollInterval = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _endpoint = endpoint;
        _registration = registration;
        _sync = sync;
        _printing = printing;
        _node = node;
        _encryption = encryption;
        _dialogs = dialogs;
        _backup = backup;
        _ui = ui ?? (a => MainThread.BeginInvokeOnMainThread(a));
        _quit = quit ?? (() => AppRestart.RestartOrQuit());
        _version = appVersion;
        _machine = machineName;
        _clock = clock ?? TimeProvider.System;
        _pollInterval = connectionPollInterval ?? TimeSpan.FromSeconds(30);
        _delay = delay ?? Task.Delay;

        SaveUrlCommand = new Command(async () => await SaveUrlAsync());
        TestUrlCommand = new Command(async () => await TestUrlAsync());
        ResetUrlCommand = new Command(async () => await ResetUrlAsync());
        RecheckCommand = new Command(async () => await RecheckConnectionAsync());
        RegisterCommand = new Command(async () => await RegisterAsync());
        SyncNowCommand = new Command(async () => await SyncNowAsync());
        SavePrintingCommand = new Command(SavePrinting);
        BackupCommand = new Command(async () => await BackupAsync());
        RestoreCommand = new Command(async () => await RestoreAsync());

        foreach (var (kind, label) in DocumentPrintSettingsExtensions.ConfigurableKinds)
            Documents.Add(new DocumentPrinterRow { Kind = kind, Label = label });
        _syncStatus = _sync.Current;
        Load();
    }

    // ── INPC ──
    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(n);
        return true;
    }

    bool _attached;
    /// <summary>Start / stop listening to sync status (page appearing / disappearing, so a transient page never leaks the subscription).</summary>
    public void Attach() { if (_attached) return; _attached = true; _syncStatus = _sync.Current; _sync.Changed += OnSyncChanged; Raise(nameof(SyncStatusText)); Raise(nameof(SyncHasProblem)); }
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _sync.Changed -= OnSyncChanged;
        StopConnectionWatch();
    }

    // ── sync server ──
    string _syncUrl = "";
    string? _urlMessage;
    bool _urlOk = true;
    public string SyncUrl { get => _syncUrl; set => Set(ref _syncUrl, value); }
    public string DefaultSyncUrl => _endpoint.DefaultBaseUrl;
    public bool IsCustomUrl => _endpoint.IsCustom;
    public string? UrlMessage { get => _urlMessage; private set { Set(ref _urlMessage, value); Raise(nameof(HasUrlMessage)); } }
    public bool HasUrlMessage => !string.IsNullOrEmpty(_urlMessage);
    public bool UrlMessageIsError { get => !_urlOk; }
    public string UrlHint => "Full address such as http://localhost:5055. Leave empty for local-only (nothing is sent).";

    public ICommand SaveUrlCommand { get; }
    public ICommand TestUrlCommand { get; }
    public ICommand ResetUrlCommand { get; }

    void SetUrlMessage(string? msg, bool ok) { _urlOk = ok; Raise(nameof(UrlMessageIsError)); UrlMessage = msg; }

    public async Task SaveUrlAsync()
    {
        if (!await RunAsync(nameof(IsUrlBusy), v => IsUrlBusy = v)) return;
        try
        {
            var r = await _endpoint.SetBaseUrlAsync(SyncUrl);
            if (r.Succeeded)
            {
                SyncUrl = r.BaseUrl ?? "";
                SetUrlMessage(string.IsNullOrEmpty(SyncUrl) ? "Saved: this computer works local-only." : "Saved. Sync uses " + SyncUrl + " now.", true);
            }
            else SetUrlMessage(r.Error ?? "Could not save the address.", false);
            Raise(nameof(IsCustomUrl));
            if (r.Succeeded) await RecheckConnectionAsync();
        }
        catch (Exception ex) { SetUrlMessage(ex.Message, false); }
        finally { IsUrlBusy = false; }
    }

    public async Task TestUrlAsync()
    {
        if (!await RunAsync(nameof(IsUrlBusy), v => IsUrlBusy = v)) return;
        try
        {
            var ok = await _endpoint.TestConnectionAsync(string.IsNullOrWhiteSpace(SyncUrl) ? null : SyncUrl);
            SetUrlMessage(ok ? "Reachable." : "Could not reach the server at that address.", ok);
        }
        catch (Exception ex) { SetUrlMessage(ex.Message, false); }
        finally { IsUrlBusy = false; }
    }

    public async Task ResetUrlAsync()
    {
        if (!await RunAsync(nameof(IsUrlBusy), v => IsUrlBusy = v)) return;
        try
        {
            var r = await _endpoint.ResetAsync();
            SyncUrl = _endpoint.BaseUrl;
            SetUrlMessage(r.Succeeded ? "Back to the default address." : r.Error, r.Succeeded);
            Raise(nameof(IsCustomUrl));
            if (r.Succeeded) await RecheckConnectionAsync();
        }
        catch (Exception ex) { SetUrlMessage(ex.Message, false); }
        finally { IsUrlBusy = false; }
    }

    bool _urlBusy;
    public bool IsUrlBusy { get => _urlBusy; private set { Set(ref _urlBusy, value); if (!value) _running.Remove(nameof(IsUrlBusy)); Raise(nameof(IsUrlIdle)); } }
    public bool IsUrlIdle => !_urlBusy;

    // ── live API reachability (configured BaseUrl; empty = local-only) ──
    public enum ApiConnectionState { Checking, Online, Offline, LocalOnly }

    ApiConnectionState _connectionState = ApiConnectionState.Checking;
    DateTimeOffset? _lastChecked;
    bool _connectionBusy;
    public ApiConnectionState ConnectionState { get => _connectionState; private set { if (Set(ref _connectionState, value)) { Raise(nameof(ConnectionStatusText)); Raise(nameof(LastCheckedText)); } } }
    public string ConnectionStatusText => ConnectionState switch
    {
        ApiConnectionState.Online => "Online",
        ApiConnectionState.Offline => "Offline",
        ApiConnectionState.LocalOnly => "Local-only",
        _ => "Checking…",
    };
    public string LastCheckedText
    {
        get
        {
            if (ConnectionState == ApiConnectionState.LocalOnly) return "No API URL is configured (local-only).";
            if (_lastChecked is not { } at) return ConnectionState == ApiConnectionState.Checking ? "Checking…" : "Not checked yet.";
            var local = TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local);
            return "Last checked: " + local.ToString("h:mm:ss tt", CultureInfo.GetCultureInfo("en-PH"));
        }
    }
    public ICommand RecheckCommand { get; }
    public bool IsConnectionBusy { get => _connectionBusy; private set { Set(ref _connectionBusy, value); Raise(nameof(IsConnectionIdle)); } }
    public bool IsConnectionIdle => !_connectionBusy;

    public async Task RecheckConnectionAsync()
    {
        var url = _endpoint.BaseUrl;
        if (string.IsNullOrEmpty(url))
        {
            ApplyConnection(ApiConnectionState.LocalOnly, checkedAt: null);
            return;
        }
        IsConnectionBusy = true;
        ApplyConnection(ApiConnectionState.Checking, _lastChecked);
        try
        {
            var ok = await _endpoint.TestConnectionAsync(url);
            ApplyConnection(ok ? ApiConnectionState.Online : ApiConnectionState.Offline, _clock.GetUtcNow());
        }
        catch (Exception) when (true)
        {
            ApplyConnection(ApiConnectionState.Offline, _clock.GetUtcNow());
        }
        finally { IsConnectionBusy = false; }
    }

    void ApplyConnection(ApiConnectionState state, DateTimeOffset? checkedAt)
    {
        _ui(() =>
        {
            _lastChecked = checkedAt;
            ConnectionState = state;
            Raise(nameof(LastCheckedText));
        });
    }

    void StopConnectionWatch()
    {
        _connectionWatch?.Cancel();
        _connectionWatch?.Dispose();
        _connectionWatch = null;
    }

    void StartConnectionPoll()
    {
        StopConnectionWatch();
        if (_pollInterval <= TimeSpan.Zero) return;
        var cts = new CancellationTokenSource();
        _connectionWatch = cts;
        _ = PollConnectionAsync(cts.Token);
    }

    async Task PollConnectionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _delay(_pollInterval, ct); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await RecheckConnectionAsync();
        }
    }

    // ── register node ──
    string _adminToken = "";
    string _nodeName = "";
    string? _registrationStatus;
    string? _registerError;
    bool _registerBusy;
    public string AdminToken { get => _adminToken; set => Set(ref _adminToken, value); }
    public string NodeName { get => _nodeName; set => Set(ref _nodeName, value); }
    public string? RegistrationStatus { get => _registrationStatus; private set => Set(ref _registrationStatus, value); }
    public string? RegisterError { get => _registerError; private set { Set(ref _registerError, value); Raise(nameof(HasRegisterError)); } }
    public bool HasRegisterError => !string.IsNullOrEmpty(_registerError);
    public bool IsRegisterBusy { get => _registerBusy; private set { Set(ref _registerBusy, value); if (!value) _running.Remove(nameof(IsRegisterBusy)); Raise(nameof(IsRegisterIdle)); } }
    public bool IsRegisterIdle => !_registerBusy;
    public ICommand RegisterCommand { get; }

    public async Task RefreshRegistrationAsync()
    {
        try
        {
            RegistrationStatus = await _registration.IsRegisteredAsync()
                ? "This computer is registered for cloud sync. Registering again replaces its secret."
                : "This computer is not registered for cloud sync yet.";
        }
        catch (Exception ex) { RegistrationStatus = "Could not read registration status: " + ex.Message; }
    }

    public async Task RegisterAsync()
    {
        RegisterError = null;
        var token = AdminToken?.Trim() ?? "";
        var name = NodeName?.Trim() ?? "";
        if (token.Length == 0 || name.Length == 0) { RegisterError = "Admin access token and computer name are required."; return; }
        if (!await RunAsync(nameof(IsRegisterBusy), v => IsRegisterBusy = v)) return;
        try
        {
            var platform = $"{DeviceName()}".Trim();
            var r = await _registration.RegisterAsync(token, name, string.IsNullOrWhiteSpace(platform) ? null : platform, string.IsNullOrWhiteSpace(_version) ? null : _version);
            if (r.Succeeded)
            {
                RegistrationStatus = r.NodeId is { } id ? $"Registered. Node id: {id}" : "Registered.";
                AdminToken = "";
                _sync.RequestSyncIfAvailable();
            }
            else RegisterError = r.Error ?? "Registration failed.";
        }
        catch (Exception ex) { RegisterError = ex.Message; }
        finally { IsRegisterBusy = false; }
    }

    static string DeviceName() => Environment.OSVersion.Platform.ToString() + " " + Environment.OSVersion.VersionString;

    // ── sync now ──
    SyncStatus _syncStatus;
    string? _syncResult;
    bool _syncBusy;
    public string SyncStatusText => _syncStatus.Banner;
    public bool SyncHasProblem => _syncStatus.Phase is SyncPhase.Error or SyncPhase.Revoked or SyncPhase.NeedsUpdate or SyncPhase.NotRegistered;
    public string? SyncResult { get => _syncResult; private set => Set(ref _syncResult, value); }
    public bool IsSyncBusy { get => _syncBusy; private set { Set(ref _syncBusy, value); if (!value) _running.Remove(nameof(IsSyncBusy)); Raise(nameof(IsSyncIdle)); } }
    public bool IsSyncIdle => !_syncBusy;
    public ICommand SyncNowCommand { get; }

    void OnSyncChanged(SyncStatus s) => _ui(() =>
    {
        _syncStatus = s;
        Raise(nameof(SyncStatusText));
        Raise(nameof(SyncHasProblem));
    });

    public async Task SyncNowAsync()
    {
        SyncResult = null;
        if (!await RunAsync(nameof(IsSyncBusy), v => IsSyncBusy = v)) return;
        try
        {
            var r = await _sync.RunOnceAsync();
            SyncResult = r.Succeeded
                ? $"Synced: sent {r.Pushed}, received {r.Pulled}."
                : r.Failure switch
                {
                    SyncFailureKind.Offline => "Offline: could not reach the sync server. Changes stay safe on this computer.",
                    SyncFailureKind.Unauthorized => "The cloud did not accept this computer's credentials. Register this node again.",
                    SyncFailureKind.Revoked => "An admin disconnected this computer from the cloud.",
                    SyncFailureKind.VersionMismatch => "Update the app to resume cloud sync.",
                    _ => "Sync failed" + (string.IsNullOrWhiteSpace(r.Error) ? "." : ": " + r.Error),
                };
        }
        catch (Exception ex) { SyncResult = "Sync failed: " + ex.Message; }
        finally { IsSyncBusy = false; }
    }

    // ── printing ──
    public ObservableCollection<DocumentPrinterRow> Documents { get; } = [];
    QueueSlipStyle _slipStyle;
    ThermalTransport _transport;
    string? _thermalName, _thermalHost, _formsPrinter;
    string _thermalPort = "9100";
    bool _showDialog, _cut, _keepPdf;
    string? _printingMessage;
    public QueueSlipStyle SlipStyle { get => _slipStyle; set => Set(ref _slipStyle, value); }
    public ThermalTransport Transport { get => _transport; set => Set(ref _transport, value); }
    public string? ThermalPrinterName { get => _thermalName; set => Set(ref _thermalName, value); }
    public string? ThermalPrinterHost { get => _thermalHost; set => Set(ref _thermalHost, value); }
    public string ThermalPrinterPort { get => _thermalPort; set => Set(ref _thermalPort, value); }
    public string? FormsPrinterName { get => _formsPrinter; set => Set(ref _formsPrinter, value); }
    public bool ShowPrintDialog { get => _showDialog; set => Set(ref _showDialog, value); }
    public bool ThermalCut { get => _cut; set => Set(ref _cut, value); }
    public bool KeepPdfCopies { get => _keepPdf; set => Set(ref _keepPdf, value); }
    public string? PrintingMessage { get => _printingMessage; private set => Set(ref _printingMessage, value); }
    public ICommand SavePrintingCommand { get; }

    void LoadPrinting()
    {
        SlipStyle = _printing.QueueSlipStyle;
        Transport = _printing.ThermalTransport;
        ThermalPrinterName = _printing.ThermalPrinterName;
        ThermalPrinterHost = _printing.ThermalPrinterHost;
        ThermalPrinterPort = _printing.ThermalPrinterPort.ToString(CultureInfo.InvariantCulture);
        FormsPrinterName = _printing.FormsPrinterName;
        ShowPrintDialog = _printing.ShowDialog;
        ThermalCut = _printing.ThermalCut;
        KeepPdfCopies = _printing.KeepPdfCopies;
        foreach (var row in Documents)
        {
            _printing.Documents.TryGetValue(row.Kind, out var d);
            row.PrinterName = d?.PrinterName;
            row.Paper = d?.Paper ?? PrinterPaper.Auto;
            row.DialogChoice = d?.ShowDialog is null ? 0 : d.ShowDialog.Value ? 1 : 2;
        }
    }

    /// <summary>Validates, writes to the live PrintingOptions (MauiPrinter reads it per job) and to printing.json.</summary>
    public void SavePrinting()
    {
        if (!int.TryParse(ThermalPrinterPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            PrintingMessage = "Thermal printer port must be a number from 1 to 65535.";
            return;
        }
        if (Transport == ThermalTransport.EscPosNetwork && string.IsNullOrWhiteSpace(ThermalPrinterHost))
        {
            PrintingMessage = "Enter the thermal printer's network address, or pick another connection type.";
            return;
        }
        _printing.QueueSlipStyle = SlipStyle;
        _printing.ThermalTransport = Transport;
        _printing.ThermalPrinterName = Blank(ThermalPrinterName);
        _printing.ThermalPrinterHost = Blank(ThermalPrinterHost);
        _printing.ThermalPrinterPort = port;
        _printing.FormsPrinterName = Blank(FormsPrinterName);
        _printing.ShowDialog = ShowPrintDialog;
        _printing.ThermalCut = ThermalCut;
        _printing.KeepPdfCopies = KeepPdfCopies;
        _printing.Documents.Clear();
        foreach (var row in Documents)
        {
            if (Blank(row.PrinterName) is null && row.Paper == PrinterPaper.Auto && row.DialogChoice == 0) continue;
            _printing.Documents[row.Kind] = new DocumentPrintSettings
            {
                PrinterName = Blank(row.PrinterName),
                Paper = row.Paper,
                ShowDialog = row.DialogChoice switch { 1 => true, 2 => false, _ => null },
            };
        }
        try
        {
            _printing.Save(_node.DataDirectory);
            PrintingMessage = "Printer settings saved.";
        }
        catch (Exception ex) { PrintingMessage = "Could not save printer settings: " + ex.Message; }
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── encryption / backup ──
    string? _backupMessage;
    bool _backupError, _backupBusy;
    public string EncryptionStatus => _encryption.State switch
    {
        DatabaseEncryptionState.Migrated or DatabaseEncryptionState.AlreadyEncrypted or DatabaseEncryptionState.NewDatabase =>
            "Patient database is encrypted on this computer (SQLCipher; the key is kept in the system keychain).",
        DatabaseEncryptionState.KeyStoreUnavailable => "The system keychain was not available, so the database is NOT encrypted.",
        _ => "The database is not encrypted yet (this build has no encryption library).",
    };
    public bool IsEncrypted => _encryption.Encrypted;
    public string? BackupMessage { get => _backupMessage; private set => Set(ref _backupMessage, value); }
    public bool BackupMessageIsError { get => _backupError; private set => Set(ref _backupError, value); }
    public bool IsBackupBusy { get => _backupBusy; private set { Set(ref _backupBusy, value); if (!value) _running.Remove(nameof(IsBackupBusy)); Raise(nameof(IsBackupIdle)); } }
    public bool IsBackupIdle => !_backupBusy;
    public bool CanBackup => _backup is not null;
    public ICommand BackupCommand { get; }
    public ICommand RestoreCommand { get; }

    void BackupResult(string message, bool error) { BackupMessageIsError = error; BackupMessage = message; }

    public async Task BackupAsync()
    {
        if (_backup is null) { BackupResult("Backup is not available on this platform.", true); return; }
        var pw = await _dialogs.PromptPasswordAsync("Backup password",
            $"Choose a password for this backup (at least {BackupFormat.MinPasswordLength} characters). You need it to restore. It is not stored anywhere.");
        if (pw is null) return;
        try { BackupFormat.ValidatePassword(pw); }
        catch (ArgumentException ex) { BackupResult(ex.Message.Split('\n')[0].Replace(" (Parameter 'password')", ""), true); return; }
        var again = await _dialogs.PromptPasswordAsync("Confirm password", "Type the backup password again.");
        if (again is null) return;
        if (again != pw) { BackupResult("The two passwords do not match.", true); return; }
        if (!await RunAsync(nameof(IsBackupBusy), v => IsBackupBusy = v)) return;
        try
        {
            var where = await _backup.ExportAsync(pw);
            if (where is null) BackupResult("Backup cancelled.", false);
            else BackupResult("Backup saved: " + where, false);
        }
        catch (Exception ex) { BackupResult("Backup failed: " + ex.Message, true); }
        finally { IsBackupBusy = false; }
    }

    public async Task RestoreAsync()
    {
        if (_backup is null) { BackupResult("Restore is not available on this platform.", true); return; }
        var pw = await _dialogs.PromptPasswordAsync("Backup password", "Type the password of the backup you want to restore.");
        if (pw is null) return;
        if (!await RunAsync(nameof(IsBackupBusy), v => IsBackupBusy = v)) return;
        BackupVerification? verified = null;
        try
        {
            verified = await _backup.PickAndVerifyAsync(pw);
            if (verified is null) { BackupResult("Restore cancelled.", false); return; }
            var h = verified.Header;
            var when = h.CreatedAt.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var ok = await _dialogs.ConfirmAsync("Restore this backup?",
                $"Backup made {when} (Asia/Manila) on {(string.IsNullOrWhiteSpace(h.Machine) ? "another computer" : h.Machine)}.\n\n" +
                "Everything on THIS computer is replaced by the backup, including changes not yet synced. " +
                "The current database is kept next to it as clinic-node.db.pre-restore-… until the next restore.\n\nThe app closes afterwards.",
                "Replace and close", "Cancel");
            if (!ok) { BackupResult("Restore cancelled. Nothing was changed.", false); return; }
            var result = await _backup.RestoreAsync(verified);
            verified = null;
            BackupResult("Restored. The app is closing.", false);
            if (!AppRestart.CanRelaunch)
                await _dialogs.InformAsync("Restore complete", "Your backup is in place. ClinicNode will close now; open it again to continue.");
            _quit();
        }
        catch (BackupFormatException ex) { BackupResult(ex.Message, true); }
        catch (Exception ex) { BackupResult("Restore failed: " + ex.Message + " Nothing was changed.", true); }
        finally
        {
            if (verified is not null) BackupService.Discard(verified);
            IsBackupBusy = false;
        }
    }

    // ── lifecycle ──
    public void Load()
    {
        SyncUrl = _endpoint.BaseUrl;
        NodeName = string.IsNullOrWhiteSpace(_machine) ? "Clinic Node" : _machine;
        LoadPrinting();
        Raise(nameof(IsCustomUrl));
        Raise(nameof(EncryptionStatus));
    }

    public async Task OnAppearingAsync()
    {
        Attach();
        Load();
        await RefreshRegistrationAsync();
        try { await _sync.RefreshStatusAsync(); } catch { /* banner keeps the last known value */ }
        await RecheckConnectionAsync();
        StartConnectionPoll();
    }

    /// <summary>Single-flight guard per section so double taps do nothing.</summary>
    readonly HashSet<string> _running = [];
    Task<bool> RunAsync(string key, Action<bool> setBusy)
    {
        if (!_running.Add(key)) return Task.FromResult(false);
        setBusy(true);
        return Task.FromResult(true);
    }
}

static class SyncServiceExtensions
{
    /// <summary>ISyncService has no RequestSync; ISyncStatus (the same SyncService object) does.</summary>
    public static void RequestSyncIfAvailable(this ISyncService s) => (s as ISyncStatus)?.RequestSync();
}
//JJQR - END
