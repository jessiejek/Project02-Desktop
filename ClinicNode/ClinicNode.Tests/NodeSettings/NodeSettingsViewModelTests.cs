//JJQR - AI (Nov 21, 2026) - NodeSettingsViewModel with fake sync/endpoint/registration/dialogs; the real BackupService on a temp DB for backup & restore.
using ClinicNode.App.Services.Backup;
using ClinicNode.App.ViewModels.Settings.Node;
using ClinicNode.Core.NodeSettings;
using ClinicNode.Core.Printing;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Wire;

namespace ClinicNode.Tests.NodeSettings;

sealed class FakeEndpoint : ISyncEndpoint
{
    public string BaseUrl { get; set; } = "http://localhost:5055";
    public string DefaultBaseUrl => "http://localhost:5055";
    public bool IsCustom { get; set; }
    public event Action<string>? Changed;
    public bool Reachable = true;
    public string? TestedUrl;
    public int Tests;
    public TaskCompletionSource<bool>? Gate;
    public Task<SyncEndpointResult> SetBaseUrlAsync(string? url, CancellationToken ct = default)
    {
        url = url?.Trim() ?? "";
        if (url.Length > 0 && !Uri.TryCreate(url, UriKind.Absolute, out var u)) return Task.FromResult(new SyncEndpointResult(false, "Enter a full address such as http://localhost:5055", BaseUrl));
        BaseUrl = url.TrimEnd('/');
        IsCustom = true;
        Changed?.Invoke(BaseUrl);
        return Task.FromResult(new SyncEndpointResult(true, null, BaseUrl));
    }
    public Task<SyncEndpointResult> ResetAsync(CancellationToken ct = default) { BaseUrl = DefaultBaseUrl; IsCustom = false; return Task.FromResult(new SyncEndpointResult(true, null, BaseUrl)); }
    public async Task<bool> TestConnectionAsync(string? url = null, CancellationToken ct = default)
    {
        Tests++;
        TestedUrl = url;
        if (Gate is not null) return await Gate.Task.WaitAsync(ct);
        return Reachable;
    }
}

sealed class FakeRegistration : ISyncNodeRegistration
{
    public bool Registered;
    public SyncRegistrationResult Result = new(true, Guid.Parse("11111111-2222-3333-4444-555555555555"), null);
    public (string Token, string Name)? Last;
    public Task<bool> IsRegisteredAsync(CancellationToken ct = default) => Task.FromResult(Registered);
    public Task<SyncRegistrationResult> RegisterAsync(string adminAccessToken, string name, string? platform, string? appVersion, CancellationToken ct = default)
    { Last = (adminAccessToken, name); return Task.FromResult(Result); }
    public Task<HandshakeResponseDto?> HandshakeAsync(CancellationToken ct = default) => Task.FromResult<HandshakeResponseDto?>(null);
}

sealed class FakeSync : ISyncService
{
    public SyncStatus Current { get; private set; } = new() { Phase = SyncPhase.Idle };
    public event Action<SyncStatus>? Changed;
    public SyncCycleResult Next = new(true, SyncFailureKind.None, null) { Pushed = 2, Pulled = 5 };
    public int Runs;
    public Task<SyncStatus> RefreshStatusAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public Task<SyncCycleResult> RunOnceAsync(CancellationToken ct = default) { Runs++; return Task.FromResult(Next); }
    public void Raise(SyncStatus s) { Current = s; Changed?.Invoke(s); }
    public int Listeners => Changed?.GetInvocationList().Length ?? 0;
}

sealed class FakeDialogs : INodeSettingsDialogs
{
    public Queue<string?> Passwords = new();
    public bool Confirm = true;
    public List<string> Informed = [];
    public string? ConfirmText;
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) { ConfirmText = message; return Task.FromResult(Confirm); }
    public Task<string?> PromptPasswordAsync(string title, string message) => Task.FromResult(Passwords.Count > 0 ? Passwords.Dequeue() : null);
    public Task InformAsync(string title, string message) { Informed.Add(title); return Task.CompletedTask; }
}

sealed class FakeFiles : IBackupFileExchange
{
    public byte[]? Exported;
    public string? ExportName;
    public string? PickFrom;
    public bool CancelExport;
    public async Task<string?> ExportAsync(string tempFile, string suggestedName, CancellationToken ct = default)
    {
        if (CancelExport) return null;
        Exported = await File.ReadAllBytesAsync(tempFile, ct);
        ExportName = suggestedName;
        return "/picked/" + suggestedName;
    }
    public Task<string?> PickBackupAsync(CancellationToken ct = default)
    {
        if (PickFrom is null) return Task.FromResult<string?>(null);
        var copy = PickFrom + ".picked";
        File.Copy(PickFrom, copy, true);
        return Task.FromResult<string?>(copy);
    }
}

public class NodeSettingsViewModelTests
{
    sealed class Rig : IAsyncDisposable
    {
        public TestHost Host = null!;
        public FakeEndpoint Endpoint = new();
        public FakeRegistration Registration = new();
        public FakeSync Sync = new();
        public FakeDialogs Dialogs = new();
        public FakeFiles Files = new();
        public PrintingOptions Printing = new();
        public NodeOptions Node = null!;
        public NodeBackupCoordinator Coordinator = null!;
        public NodeSettingsViewModel Vm = null!;
        public int Quits;

        public static async Task<Rig> StartAsync(TimeSpan? poll = null, Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? clock = null)
        {
            var r = new Rig { Host = await TestHost.StartAsync() };
            r.Node = r.Host.Get<NodeOptions>();
            r.Coordinator = new NodeBackupCoordinator(r.Node, DatabaseEncryptionResult.Plaintext, r.Files, cacheDir: r.Node.DataDirectory, appVersion: "1.0", machine: "test-mac");
            r.Vm = new NodeSettingsViewModel(r.Endpoint, r.Registration, r.Sync, r.Printing, r.Node, DatabaseEncryptionResult.Plaintext, r.Dialogs, r.Coordinator,
                ui: a => a(), quit: () => r.Quits++, appVersion: "1.0", machineName: "test-mac",
                clock: clock, connectionPollInterval: poll ?? TimeSpan.Zero, delay: delay);
            return r;
        }
        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    // ── sync URL ──
    [Fact]
    public async Task Url_field_starts_with_the_live_endpoint_and_saves_through_ISyncEndpoint()
    {
        await using var r = await Rig.StartAsync();
        Assert.Equal("http://localhost:5055", r.Vm.SyncUrl);
        r.Vm.SyncUrl = "http://192.168.1.20:5055/";
        await r.Vm.SaveUrlAsync();
        Assert.Equal("http://192.168.1.20:5055", r.Endpoint.BaseUrl);
        Assert.Equal("http://192.168.1.20:5055", r.Vm.SyncUrl);      // shows the normalised value
        Assert.True(r.Vm.IsCustomUrl);
        Assert.False(r.Vm.UrlMessageIsError);
        Assert.Contains("Saved", r.Vm.UrlMessage);
    }

    [Fact]
    public async Task Empty_url_saves_as_local_only_and_a_bad_url_shows_the_endpoint_error()
    {
        await using var r = await Rig.StartAsync();
        r.Vm.SyncUrl = "";
        await r.Vm.SaveUrlAsync();
        Assert.Equal("", r.Endpoint.BaseUrl);
        Assert.Contains("local-only", r.Vm.UrlMessage);
        r.Vm.SyncUrl = "not a url";
        await r.Vm.SaveUrlAsync();
        Assert.True(r.Vm.UrlMessageIsError);
        Assert.Contains("full address", r.Vm.UrlMessage);
    }

    [Fact]
    public async Task Test_connection_reports_reachable_and_unreachable_without_saving()
    {
        await using var r = await Rig.StartAsync();
        r.Vm.SyncUrl = "http://other:1";
        await r.Vm.TestUrlAsync();
        Assert.Equal("http://other:1", r.Endpoint.TestedUrl);
        Assert.Equal("http://localhost:5055", r.Endpoint.BaseUrl);
        Assert.False(r.Vm.UrlMessageIsError);
        r.Endpoint.Reachable = false;
        await r.Vm.TestUrlAsync();
        Assert.True(r.Vm.UrlMessageIsError);
        Assert.Contains("Could not reach", r.Vm.UrlMessage);
    }

    [Fact]
    public async Task Use_default_resets_the_endpoint_and_the_field()
    {
        await using var r = await Rig.StartAsync();
        r.Vm.SyncUrl = "http://custom:9";
        await r.Vm.SaveUrlAsync();
        await r.Vm.ResetUrlAsync();
        Assert.Equal("http://localhost:5055", r.Vm.SyncUrl);
        Assert.False(r.Vm.IsCustomUrl);
    }

    // ── register ──
    [Fact]
    public async Task Recheck_reports_online_offline_and_local_only_against_the_configured_url()
    {
        await using var r = await Rig.StartAsync();
        r.Endpoint.BaseUrl = "";
        await r.Vm.RecheckConnectionAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.LocalOnly, r.Vm.ConnectionState);
        Assert.Equal("Local-only", r.Vm.ConnectionStatusText);
        Assert.Contains("local-only", r.Vm.LastCheckedText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, r.Endpoint.Tests);

        r.Endpoint.BaseUrl = "http://clinic.example";
        r.Endpoint.Reachable = true;
        await r.Vm.RecheckConnectionAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.Online, r.Vm.ConnectionState);
        Assert.Equal("Online", r.Vm.ConnectionStatusText);
        Assert.StartsWith("Last checked:", r.Vm.LastCheckedText);
        Assert.Equal("http://clinic.example", r.Endpoint.TestedUrl);
        Assert.Equal(1, r.Endpoint.Tests);

        r.Endpoint.Reachable = false;
        await r.Vm.RecheckConnectionAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.Offline, r.Vm.ConnectionState);
        Assert.Equal("Offline", r.Vm.ConnectionStatusText);
        Assert.Equal(2, r.Endpoint.Tests);
    }

    [Fact]
    public async Task Recheck_shows_checking_until_the_probe_finishes()
    {
        await using var r = await Rig.StartAsync();
        r.Endpoint.Gate = new TaskCompletionSource<bool>();
        var pending = r.Vm.RecheckConnectionAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.Checking, r.Vm.ConnectionState);
        Assert.Equal("Checking…", r.Vm.ConnectionStatusText);
        Assert.True(r.Vm.IsConnectionBusy);
        Assert.False(r.Vm.IsConnectionIdle);
        r.Endpoint.Gate.SetResult(true);
        await pending;
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.Online, r.Vm.ConnectionState);
        Assert.False(r.Vm.IsConnectionBusy);
    }

    [Fact]
    public async Task Saving_or_appearing_refreshes_connection_state()
    {
        await using var r = await Rig.StartAsync();
        r.Vm.SyncUrl = "";
        await r.Vm.SaveUrlAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.LocalOnly, r.Vm.ConnectionState);
        Assert.Equal(0, r.Endpoint.Tests);

        r.Vm.SyncUrl = "http://saved:5055";
        r.Endpoint.Reachable = true;
        await r.Vm.SaveUrlAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.Online, r.Vm.ConnectionState);
        Assert.Equal("http://saved:5055", r.Endpoint.TestedUrl);

        r.Endpoint.Reachable = false;
        r.Endpoint.Tests = 0;
        await r.Vm.OnAppearingAsync();
        Assert.Equal(NodeSettingsViewModel.ApiConnectionState.Offline, r.Vm.ConnectionState);
        Assert.True(r.Endpoint.Tests >= 1);
        r.Vm.Detach();
    }

    [Fact]
    public async Task Poll_rechecks_while_the_page_is_visible_and_stops_after_detach()
    {
        TaskCompletionSource? pending = null;
        Func<TimeSpan, CancellationToken, Task> delay = async (_, ct) =>
        {
            var tcs = new TaskCompletionSource();
            pending = tcs;
            await tcs.Task.WaitAsync(ct);
        };
        await using var r = await Rig.StartAsync(poll: TimeSpan.FromMilliseconds(1), delay: delay);
        await r.Vm.OnAppearingAsync();
        var afterAppear = r.Endpoint.Tests;
        Assert.True(afterAppear >= 1);
        var until = DateTime.UtcNow.AddSeconds(3);
        while (pending is null && DateTime.UtcNow < until)
            await Task.Delay(10);
        Assert.NotNull(pending);
        pending!.SetResult();
        until = DateTime.UtcNow.AddSeconds(3);
        while (r.Endpoint.Tests == afterAppear && DateTime.UtcNow < until)
            await Task.Delay(20);
        Assert.True(r.Endpoint.Tests > afterAppear, "expected a poll recheck");
        r.Vm.Detach();
        var afterDetach = r.Endpoint.Tests;
        await Task.Delay(80);
        Assert.Equal(afterDetach, r.Endpoint.Tests);
    }

    [Fact]
    public async Task Register_needs_token_and_name_then_calls_RegisterAsync_and_clears_the_token()
    {
        await using var r = await Rig.StartAsync();
        r.Vm.AdminToken = "";
        await r.Vm.RegisterAsync();
        Assert.True(r.Vm.HasRegisterError);
        Assert.Null(r.Registration.Last);

        r.Vm.AdminToken = " eyJ.token ";
        r.Vm.NodeName = " Front desk ";
        await r.Vm.RegisterAsync();
        Assert.Equal(("eyJ.token", "Front desk"), r.Registration.Last);
        Assert.False(r.Vm.HasRegisterError);
        Assert.StartsWith("Registered. Node id: 11111111", r.Vm.RegistrationStatus);
        Assert.Equal("", r.Vm.AdminToken);
        Assert.True(r.Vm.IsRegisterIdle);
    }

    [Fact]
    public async Task Register_failure_shows_the_server_message_and_keeps_the_token_for_a_retry()
    {
        await using var r = await Rig.StartAsync();
        r.Registration.Result = new SyncRegistrationResult(false, null, "Token expired");
        r.Vm.AdminToken = "abc";
        r.Vm.NodeName = "pc";
        await r.Vm.RegisterAsync();
        Assert.Equal("Token expired", r.Vm.RegisterError);
        Assert.Equal("abc", r.Vm.AdminToken);
        Assert.True(r.Vm.IsRegisterIdle);
    }

    [Fact]
    public async Task Registration_status_reflects_IsRegistered()
    {
        await using var r = await Rig.StartAsync();
        await r.Vm.RefreshRegistrationAsync();
        Assert.Contains("not registered", r.Vm.RegistrationStatus);
        r.Registration.Registered = true;
        await r.Vm.RefreshRegistrationAsync();
        Assert.Contains("is registered", r.Vm.RegistrationStatus);
    }

    // ── sync now ──
    [Fact]
    public async Task Sync_now_runs_one_cycle_and_summarises_it()
    {
        await using var r = await Rig.StartAsync();
        await r.Vm.SyncNowAsync();
        Assert.Equal(1, r.Sync.Runs);
        Assert.Equal("Synced: sent 2, received 5.", r.Vm.SyncResult);
        Assert.True(r.Vm.IsSyncIdle);
    }

    [Theory]
    [InlineData(SyncFailureKind.Offline, "Offline")]
    [InlineData(SyncFailureKind.Unauthorized, "Register this node again")]
    [InlineData(SyncFailureKind.Revoked, "disconnected")]
    [InlineData(SyncFailureKind.VersionMismatch, "Update the app")]
    [InlineData(SyncFailureKind.Server, "Sync failed")]
    public async Task Sync_now_failures_get_plain_language(SyncFailureKind kind, string expected)
    {
        await using var r = await Rig.StartAsync();
        r.Sync.Next = new SyncCycleResult(false, kind, "boom");
        await r.Vm.SyncNowAsync();
        Assert.Contains(expected, r.Vm.SyncResult);
        Assert.True(r.Vm.IsSyncIdle);
    }

    [Fact]
    public async Task Status_text_follows_sync_events_only_while_attached()
    {
        await using var r = await Rig.StartAsync();
        Assert.Equal(0, r.Sync.Listeners);
        r.Vm.Attach();
        r.Vm.Attach();
        Assert.Equal(1, r.Sync.Listeners);
        r.Sync.Raise(new SyncStatus { Phase = SyncPhase.Offline, PendingCount = 3 });
        Assert.Equal("Offline — 3 waiting to sync", r.Vm.SyncStatusText);
        Assert.False(r.Vm.SyncHasProblem);
        r.Sync.Raise(new SyncStatus { Phase = SyncPhase.NotRegistered });
        Assert.True(r.Vm.SyncHasProblem);
        r.Vm.Detach();
        Assert.Equal(0, r.Sync.Listeners);
    }

    // ── printing ──
    [Fact]
    public async Task Printing_settings_load_from_the_options_and_save_per_document_choices_to_printing_json()
    {
        await using var r = await Rig.StartAsync();
        var rx = r.Vm.Documents.Single(d => d.Kind == ClinicPrintKind.Prescription);
        rx.PrinterName = " HP A5 ";
        rx.Paper = PrinterPaper.A5;
        rx.DialogChoice = 2;
        r.Vm.Documents.Single(d => d.Kind == ClinicPrintKind.Receipt).Paper = PrinterPaper.A4;
        r.Vm.SlipStyle = QueueSlipStyle.Thermal80;
        r.Vm.Transport = ThermalTransport.EscPosNetwork;
        r.Vm.ThermalPrinterHost = "192.168.1.50";
        r.Vm.ThermalPrinterPort = "9101";
        r.Vm.FormsPrinterName = "Office";
        r.Vm.SavePrinting();

        Assert.Equal("Printer settings saved.", r.Vm.PrintingMessage);
        Assert.Equal("HP A5", r.Printing.Documents[ClinicPrintKind.Prescription].PrinterName);
        Assert.Equal(false, r.Printing.Documents[ClinicPrintKind.Prescription].ShowDialog);
        Assert.Equal(PrinterPaper.A4, r.Printing.Documents[ClinicPrintKind.Receipt].Paper);
        Assert.False(r.Printing.Documents.ContainsKey(ClinicPrintKind.LabRequest)); // untouched rows are not stored
        Assert.Equal(9101, r.Printing.ThermalPrinterPort);
        Assert.Equal("Office", r.Printing.Resolve(ClinicPrintKind.LabRequest).PrinterName);

        var onDisk = PrintingOptions.Load(r.Node.DataDirectory);
        Assert.Equal(QueueSlipStyle.Thermal80, onDisk.QueueSlipStyle);
        Assert.Equal("192.168.1.50", onDisk.ThermalPrinterHost);
        Assert.Equal(PrinterPaper.A5, onDisk.Documents[ClinicPrintKind.Prescription].Paper);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("")]
    public async Task Bad_printer_port_is_refused_and_nothing_is_written(string port)
    {
        await using var r = await Rig.StartAsync();
        r.Vm.ThermalPrinterPort = port;
        r.Vm.SavePrinting();
        Assert.Contains("port", r.Vm.PrintingMessage);
        Assert.False(File.Exists(Path.Combine(r.Node.DataDirectory, PrintingOptions.FileName)));
    }

    [Fact]
    public async Task Network_thermal_without_an_address_is_refused()
    {
        await using var r = await Rig.StartAsync();
        r.Vm.Transport = ThermalTransport.EscPosNetwork;
        r.Vm.ThermalPrinterHost = " ";
        r.Vm.SavePrinting();
        Assert.Contains("network address", r.Vm.PrintingMessage);
    }

    // ── backup & restore ──
    [Fact]
    public async Task Backup_asks_twice_for_the_password_and_hands_the_encrypted_file_to_the_picker()
    {
        await using var r = await Rig.StartAsync();
        r.Dialogs.Passwords.Enqueue("correct horse");
        r.Dialogs.Passwords.Enqueue("correct horse");
        await r.Vm.BackupAsync();
        Assert.StartsWith("Backup saved: /picked/ClinicNode-backup-", r.Vm.BackupMessage);
        Assert.False(r.Vm.BackupMessageIsError);
        Assert.NotNull(r.Files.Exported);
        Assert.StartsWith("CNBACKUP", System.Text.Encoding.ASCII.GetString(r.Files.Exported!, 0, 8));
        Assert.Empty(Directory.GetFiles(r.Node.DataDirectory, "*" + BackupFormat.FileExtension)); // temp ciphertext removed
        Assert.True(r.Vm.IsBackupIdle);
    }

    [Fact]
    public async Task Backup_rejects_short_and_mismatched_passwords_and_cancellation_is_quiet()
    {
        await using var r = await Rig.StartAsync();
        r.Dialogs.Passwords.Enqueue("short");
        await r.Vm.BackupAsync();
        Assert.True(r.Vm.BackupMessageIsError);
        Assert.Null(r.Files.Exported);

        r.Dialogs.Passwords.Enqueue("correct horse");
        r.Dialogs.Passwords.Enqueue("correct horsE");
        await r.Vm.BackupAsync();
        Assert.Contains("do not match", r.Vm.BackupMessage);

        r.Dialogs.Passwords.Enqueue(null); // cancelled at the first prompt
        r.Vm.BackupAsync().Wait();
        Assert.Null(r.Files.Exported);

        r.Files.CancelExport = true;
        r.Dialogs.Passwords.Enqueue("correct horse");
        r.Dialogs.Passwords.Enqueue("correct horse");
        await r.Vm.BackupAsync();
        Assert.Equal("Backup cancelled.", r.Vm.BackupMessage);
        Assert.False(r.Vm.BackupMessageIsError);
    }

    static async Task<string> MakeBackupFile(Rig r, string pw = "correct horse")
    {
        r.Dialogs.Passwords.Enqueue(pw);
        r.Dialogs.Passwords.Enqueue(pw);
        await r.Vm.BackupAsync();
        var path = Path.Combine(r.Node.DataDirectory, "mine" + BackupFormat.FileExtension);
        await File.WriteAllBytesAsync(path, r.Files.Exported!);
        return path;
    }

    static async Task EnqueueAsync(Rig r, int n)
    {
        await using var db = await r.Host.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>().CreateDbContextAsync();
        for (var i = 0; i < n; i++)
            await r.Host.Get<ISyncOutbox>().EnqueueAsync(db, new SyncOutboxEntry { OperationId = Guid.NewGuid(), Aggregate = "consultation", EntityId = Guid.NewGuid(), OperationType = "update", PayloadJson = "{}", CreatedAt = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task Restore_confirms_swaps_the_database_and_quits()
    {
        await using var r = await Rig.StartAsync();
        await EnqueueAsync(r, 2);
        var backup = await MakeBackupFile(r);
        await EnqueueAsync(r, 4);
        r.Files.PickFrom = backup;
        r.Dialogs.Passwords.Enqueue("correct horse");

        await r.Vm.RestoreAsync();

        Assert.Contains("Everything on THIS computer is replaced", r.Dialogs.ConfirmText);
        Assert.Contains("test-mac", r.Dialogs.ConfirmText);
        Assert.Equal(1, r.Quits);
        Assert.Single(r.Dialogs.Informed); // Mac cannot relaunch itself: the user is told
        Assert.Equal(2, SqlCipher.TableCounts(r.Node.DatabasePath, null)["node_sync_outbox"]);
        Assert.Single(Directory.GetFiles(r.Node.DataDirectory, "clinic-node.db.pre-restore-*").Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm")));
        Assert.False(File.Exists(backup + ".picked"));
    }

    [Fact]
    public async Task Declining_the_confirmation_changes_nothing_and_leaves_no_plaintext_behind()
    {
        await using var r = await Rig.StartAsync();
        await EnqueueAsync(r, 2);
        var backup = await MakeBackupFile(r);
        await EnqueueAsync(r, 4);
        r.Files.PickFrom = backup;
        r.Dialogs.Passwords.Enqueue("correct horse");
        r.Dialogs.Confirm = false;

        await r.Vm.RestoreAsync();

        Assert.Equal(0, r.Quits);
        Assert.Equal(6, SqlCipher.TableCounts(r.Node.DatabasePath, null)["node_sync_outbox"]);
        Assert.Empty(Directory.GetFiles(r.Node.DataDirectory, ".*.tmp"));
        Assert.Contains("Nothing was changed", r.Vm.BackupMessage);
        Assert.True(r.Vm.IsBackupIdle);
    }

    [Fact]
    public async Task Restore_with_the_wrong_password_shows_a_message_and_changes_nothing()
    {
        await using var r = await Rig.StartAsync();
        var backup = await MakeBackupFile(r);
        r.Files.PickFrom = backup;
        r.Dialogs.Passwords.Enqueue("wrong password");
        await r.Vm.RestoreAsync();
        Assert.True(r.Vm.BackupMessageIsError);
        Assert.Contains("password", r.Vm.BackupMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, r.Quits);
        Assert.Null(r.Dialogs.ConfirmText);
    }

    [Fact]
    public async Task Restore_cancelled_in_the_picker_is_quiet()
    {
        await using var r = await Rig.StartAsync();
        r.Files.PickFrom = null;
        r.Dialogs.Passwords.Enqueue("correct horse");
        await r.Vm.RestoreAsync();
        Assert.Equal("Restore cancelled.", r.Vm.BackupMessage);
        Assert.False(r.Vm.BackupMessageIsError);
    }

    [Fact]
    public async Task Encryption_status_says_plainly_when_the_database_is_not_encrypted()
    {
        await using var r = await Rig.StartAsync();
        Assert.False(r.Vm.IsEncrypted);
        Assert.Contains("not encrypted", r.Vm.EncryptionStatus);
    }
}
//JJQR - END
