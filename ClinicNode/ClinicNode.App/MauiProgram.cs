// JJQR - AI (Nov 21, 2026) - MAUI host: local SQLite, native shell, no embedded browser.
// AddClinicNodeAppServices is the real service graph; CreateMauiApp is the platform wrapper. Tests build the same graph with file/memory stores.
using ClinicNode.App.Services;
using ClinicNode.App.Services.Files;
using ClinicNode.App.Services.Backup;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.ViewModels.Doctor.Extra;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.App.Views;
using ClinicNode.App.Views.Admin;
using ClinicNode.App.Views.Doctor;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.App.Views.Shared;
using ClinicNode.App.Views.Settings.Node;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Auth;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.NodeSettings;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Auth;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ClinicNode.App;

/// <summary>Switches used by the test host so the same graph can run without SecureStorage / a real printer.</summary>
public sealed class ClinicNodeAppHostOptions
{
    /// <summary>When true (the app), use SecureStorage + Preferences + MauiPrinter. Tests set this to false.</summary>
    public bool UsePlatformStores { get; init; } = true;
    public IEmailMemory? EmailMemory { get; init; }
}

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Inter-Regular.ttf", "Inter");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("fa-solid-900.ttf", "FASolid");
                fonts.AddFont("fa-regular-400.ttf", "FARegular");
            });

        try { SQLitePCL.Batteries_V2.Init(); }
        catch { /* bundled by EF / SQLitePCLRaw if already initialized */ }

        var dataDir = Path.Combine(FileSystem.AppDataDirectory, "ClinicNode");
        var options = new NodeOptions
        {
            DataDirectory = dataDir,
            Demo = IsDemo(),
            CloudBaseUrl = Environment.GetEnvironmentVariable("CLINIC_NODE_CLOUD_URL") ?? "",
        };
        AddClinicNodeAppServices(builder.Services, options, new ClinicNodeAppHostOptions
        {
            UsePlatformStores = true,
            EmailMemory = new PreferencesEmailMemory(),
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif
        //JJQR - AI (Nov 21, 2026) - Mac multi-line text areas have no border by default: draw one so every text area is clearly a box
#if MACCATALYST || IOS
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("ClinicEditorBorder", (handler, view) =>
        {
            var tv = handler.PlatformView;
            tv.Layer.BorderWidth = 1;
            tv.Layer.BorderColor = UIKit.UIColor.FromRGB(0xC3, 0xCB, 0xD3).CGColor;
            tv.Layer.CornerRadius = 8;
            tv.Layer.MasksToBounds = true;
            tv.BackgroundColor = UIKit.UIColor.White;
            tv.TextContainerInset = new UIKit.UIEdgeInsets(10, 8, 10, 8);
            tv.FocusEffect = null; // no heavy blue system focus ring
        });
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("ClinicEntryFocus", (handler, view) => handler.PlatformView.FocusEffect = null);
#endif
        //JJQR - END
        return builder.Build();
    }

    /// <summary>
    /// The real Clinic Node service graph (Core, staff, admin, doctor-extra, sync, printing, every Page and ViewModel).
    /// Tests call this on a plain ServiceCollection with <see cref="ClinicNodeAppHostOptions.UsePlatformStores"/> set to false.
    /// </summary>
    public static IServiceCollection AddClinicNodeAppServices(IServiceCollection services, NodeOptions options, ClinicNodeAppHostOptions? host = null)
    {
        host ??= new ClinicNodeAppHostOptions();
        var dataDir = options.DataDirectory;

        services.AddClinicNodeCore(options, host.EmailMemory ?? (host.UsePlatformStores ? new PreferencesEmailMemory() : new FileEmailMemory(dataDir)));
        services.AddClinicNodeStaff();
        services.AddSingleton<IStaffProfileService>(sp =>
            new SessionRefreshingStaffProfileService(
                ActivatorUtilities.CreateInstance<StaffProfileService>(sp),
                sp.GetRequiredService<IAuthService>()));
        services.AddClinicNodeAdmin();
        services.AddClinicNodeDoctorExtra();

        if (host.UsePlatformStores)
            services.AddSingleton<INodeCredentialStore, SecureStorageNodeCredentialStore>();
        else
            services.TryAddSingleton<INodeCredentialStore, InMemoryNodeCredentialStore>();
        services.AddClinicNodeSync();
        // JJQR - AI (Nov 21, 2026) - online-first: API client + connectivity monitor + Api-or-Local routers (docs/ONLINE-FIRST.md). The URL is the same one sync uses.
        ClinicNode.Api.OnlineFirst.ApiServiceCollectionExtensions.AddClinicNodeOnlineFirst(services, sp => sp.GetRequiredService<ISyncEndpoint>().BaseUrl);
        // JJQR - END
        //JJQR - AI (21 Nov 2026) - wave-end wiring (allen0): online-first modules. Order matters: after AddClinicNodeOnlineFirst; nothing calls AddClinicNodeAdmin() after AddAdminModule.
        ClinicNode.App.Shared.SharedModule.AddClinicNodeShared(services, host.UsePlatformStores);
        ClinicNode.App.StaffModule.AddStaffModule(services);
        ClinicNode.App.Admin.AdminModule.AddAdminModule(services);
        ClinicNode.App.Doctor.DoctorModule.AddDoctorModule(services);
        ClinicNode.App.DoctorExtraModule.AddDoctorExtraModule(services);
        ClinicNode.App.DoctorExtra2Module.AddDoctorExtra2Module(services);
        //JJQR - END

        // JJQR - AI (Nov 21, 2026) - This-computer settings: encryption status, backup coordinator, page.
        // Plaintext is the default result so tests and the current host can resolve the graph without
        // running EncryptedDatabaseStartup (which would rewrite the live clinic-node.db).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(DatabaseEncryptionResult.Plaintext);
        if (host.UsePlatformStores)
            services.AddSingleton<IBackupFileExchange, BackupFileExchange>();
        else
            services.AddSingleton<IBackupFileExchange, NullBackupFileExchange>();
        services.AddSingleton(sp => new NodeBackupCoordinator(
            sp.GetRequiredService<NodeOptions>(),
            sp.GetRequiredService<DatabaseEncryptionResult>(),
            sp.GetRequiredService<IBackupFileExchange>(),
            cacheDir: host.UsePlatformStores ? null : dataDir,
            appVersion: host.UsePlatformStores ? null : "test",
            machine: host.UsePlatformStores ? null : "test"));
        services.AddTransient<NodeSettingsPage>();
        // JJQR - END

        services.AddSingleton(ClinicNode.Core.Printing.PrintingOptions.Load(dataDir));
        services.AddSingleton<ClinicNode.Core.Print.IPrintDocumentFactory, ClinicNode.Core.Printing.ClinicFormsPrintDocumentFactory>();
        if (host.UsePlatformStores)
        {
            services.AddSingleton<MauiPrinter>();
            services.AddSingleton<IPrinter>(sp => sp.GetRequiredService<MauiPrinter>());
            services.AddSingleton<ClinicNode.Core.Printing.IReceiptPrinter>(sp => sp.GetRequiredService<MauiPrinter>());
        }
        // else the test host registers fakes after this call

        // ---- pages & view-models (staff, admin, doctor) ----
        services.AddTransient<LoginPage>();
        services.AddTransient<TodayPage>();
        services.AddTransient<NewWalkInPage>();
        services.AddTransient<ConsultationPage>();
        services.AddTransient<WalkInWizardViewModel>();
        services.AddTransient<DoctorAppointmentsViewModel>();

        services.AddTransient<StaffWalkInPage>();
        services.AddTransient<StaffPatientsPage>();
        services.AddTransient<StaffDoctorStatusPage>();
        services.AddTransient<StaffAnnouncementsPage>();
        services.AddTransient<StaffProfilePage>();
        services.AddTransient<StaffHomePage>();
        services.AddTransient<StaffQueuePage>();
        services.AddTransient<StaffPaymentsPage>();

        services.AddTransient<AdminDashboardViewModel>();
        services.AddTransient<AdminBookingsViewModel>();
        services.AddTransient<AdminBookingDetailViewModel>();
        services.AddTransient<AdminPatientsViewModel>();
        services.AddTransient<AdminPatientDetailViewModel>();
        services.AddTransient<AdminDoctorsViewModel>();
        services.AddTransient<AdminDoctorNewViewModel>();
        services.AddTransient<AdminDoctorEditViewModel>();
        services.AddTransient<AdminStaffViewModel>();
        services.AddTransient<AdminAnnouncementsViewModel>();
        services.AddTransient<AdminSettingsViewModel>();
        services.AddTransient<AdminAuditViewModel>();
        services.AddTransient<AdminReportsViewModel>();
        services.AddTransient<AdminHomePage>();
        services.AddTransient<AdminWalkInPage>();
        services.AddTransient<AdminBookingsPage>();
        services.AddTransient<AdminBookingDetailPage>();
        services.AddTransient<AdminPatientsPage>();
        services.AddTransient<AdminPatientDetailPage>();
        services.AddTransient<AdminDoctorsPage>();
        services.AddTransient<AdminDoctorNewPage>();
        services.AddTransient<AdminDoctorEditPage>();
        services.AddTransient<AdminStaffPage>();
        services.AddTransient<AdminAnnouncementsPage>();
        services.AddTransient<AdminSettingsPage>();
        services.AddTransient<AdminAuditPage>();
        services.AddTransient<AdminReportsPage>();
        services.AddTransient<AdminRegisterNodePage>();
        services.AddTransient<SyncConflictsPage>();
        // JJQR - AI (Nov 21, 2026) - IFileSaver, TopBar, allen3 doctor extra pages
        services.AddSingleton<IFileSaver, FileSaver>();
        services.AddTransient<TopBarView>();
        services.AddTransient<DoctorAppointmentsPage>();
        services.AddTransient<DoctorPatientsPage>();
        services.AddTransient<DoctorSchedulePage>();
        services.AddTransient<DoctorProfilePage>();
        services.AddTransient<DoctorTemplatesPage>();
        // JJQR - END
        services.AddSingleton<AppShell>();
        return services;
    }

    static bool IsDemo()
    {
        var env = Environment.GetEnvironmentVariable("CLINIC_NODE_DEMO");
        if (string.Equals(env, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
            return true;
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
// JJQR - END
