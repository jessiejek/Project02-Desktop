// JJQR - AI (Nov 21, 2026) - builds the real MauiProgram service graph and resolves every registered Page and ViewModel.
using System.Reflection;
using ClinicNode.App;
using ClinicNode.App.Services;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Print;
using ClinicNode.Core.Printing;
using ClinicNode.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace ClinicNode.Tests;

public class DiSmokeTests
{
    [Fact]
    public async Task App_service_graph_resolves_every_registered_page_and_view_model()
    {
        await using var host = await AppTestHost.StartAsync();
        var errors = new List<string>();

        var registered = host.Services.GetType() // built provider doesn't expose descriptors; use the catalog from the collection we kept
            ;
        foreach (var type in host.UiTypes)
        {
            try { _ = host.Services.GetRequiredService(type); }
            catch (Exception ex)
            {
                // XAML / Shell often need a running MAUI Application; still fail if a *service* is missing.
                if (IsMauiRuntimeGap(ex) && CtorServicesResolve(host.Services, type, errors))
                    continue;
                errors.Add($"{type.FullName}: {Unwrap(ex)}");
            }
        }

        // Also walk the assembly: any Page/ViewModel whose constructor is made of services must have those services registered,
        // even if the type itself is created with extra args (StaffBookingDetailPage(IServiceProvider, Guid)).
        foreach (var type in AppTestHost.DiscoverUiTypes())
        {
            CtorServicesResolve(host.Services, type, errors);
        }

        Assert.True(errors.Count == 0, "DI smoke failed:\n - " + string.Join("\n - ", errors.Distinct()));
    }

    static bool CtorServicesResolve(IServiceProvider sp, Type type, List<string> errors)
    {
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null) return true;
        var ok = true;
        foreach (var p in ctor.GetParameters())
        {
            if (!IsServiceParameter(p.ParameterType)) continue;
            try { _ = sp.GetRequiredService(p.ParameterType); }
            catch (Exception ex)
            {
                errors.Add($"{type.Name} needs {p.ParameterType.Name}: {Unwrap(ex)}");
                ok = false;
            }
        }
        return ok;
    }

    static bool IsServiceParameter(Type t)
    {
        if (t == typeof(string) || t.IsPrimitive || t == typeof(Guid) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateOnly))
            return false;
        if (t.IsEnum || t.IsValueType) return false;
        // JJQR - AI (Nov 21, 2026) - callbacks (NotificationsPage onClosed, NodeSettingsViewModel ui/quit) are not services
        if (typeof(Delegate).IsAssignableFrom(t)) return false;
        // JJQR - END
        // IServiceProvider is always present
        return true;
    }

    static bool IsMauiRuntimeGap(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message + e.GetType().Name;
            if (m.Contains("InitializeComponent", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("XAML", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("Application.Current", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("Window", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("Handler", StringComparison.OrdinalIgnoreCase) && m.Contains("Maui", StringComparison.OrdinalIgnoreCase)) return true;
            // JJQR - AI (Nov 21, 2026) - constructing a Page/Shell off-device hits Essentials stubs or no dispatcher
            if (m.Contains("ReferenceAssembly", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("portable version of this assembly", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("dispatcher", StringComparison.OrdinalIgnoreCase)) return true;
            // JJQR - END
        }
        return false;
    }

    static string Unwrap(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex.GetType().Name + ": " + ex.Message;
    }
}

/// <summary>Plain ServiceCollection running <see cref="MauiProgram.AddClinicNodeAppServices"/> against a temp SQLite folder.</summary>
public sealed class AppTestHost : IAsyncDisposable
{
    public string DataDirectory { get; }
    public ServiceProvider Services { get; }
    public IReadOnlyList<Type> UiTypes { get; }

    AppTestHost(string dir, ServiceProvider services, IReadOnlyList<Type> ui)
    {
        DataDirectory = dir;
        Services = services;
        UiTypes = ui;
    }

    public static async Task<AppTestHost> StartAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var services = new ServiceCollection();
        services.AddLogging();
        var options = new NodeOptions { DataDirectory = dir, Demo = true, CloudBaseUrl = "" };
        MauiProgram.AddClinicNodeAppServices(services, options, new ClinicNodeAppHostOptions
        {
            UsePlatformStores = false,
            EmailMemory = new FileEmailMemory(dir),
        });
        services.AddSingleton<IPrinter, RecordingPrinter>();
        services.AddSingleton<IReceiptPrinter>(sp => (IReceiptPrinter)sp.GetRequiredService<IPrinter>());
        // JJQR - AI (Nov 21, 2026) - UI dialog port; the real page implements this and is not resolved as the VM's dialogs
        services.AddSingleton<ClinicNode.App.ViewModels.Settings.Node.INodeSettingsDialogs, NullNodeSettingsDialogs>();
        // JJQR - END

        var ui = services
            .Where(d => d.ServiceType is { IsAbstract: false, IsInterface: false } && IsUi(d.ServiceType))
            .Select(d => d.ServiceType)
            .Distinct()
            .ToList();

        var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
        return new AppTestHost(dir, sp, ui);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public static IEnumerable<Type> DiscoverUiTypes() =>
        typeof(MauiProgram).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && IsUi(t));

    static bool IsUi(Type t) =>
        t.Name.EndsWith("Page", StringComparison.Ordinal) ||
        t.Name.EndsWith("ViewModel", StringComparison.Ordinal) ||
        t == typeof(App.AppShell);

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        try { Directory.Delete(DataDirectory, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class RecordingPrinter : IPrinter, IReceiptPrinter
{
    public List<PrintDocument> Documents { get; } = [];
    public List<ReceiptPrintModel> Receipts { get; } = [];
    public Task PrintAsync(PrintDocument document, CancellationToken ct = default) { Documents.Add(document); return Task.CompletedTask; }
    public Task PrintReceiptAsync(ReceiptPrintModel model, CancellationToken ct = default) { Receipts.Add(model); return Task.CompletedTask; }
}
// JJQR - END

// JJQR - AI (Nov 21, 2026)
sealed class NullNodeSettingsDialogs : ClinicNode.App.ViewModels.Settings.Node.INodeSettingsDialogs
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) => Task.FromResult(false);
    public Task<string?> PromptPasswordAsync(string title, string message) => Task.FromResult<string?>(null);
    public Task InformAsync(string title, string message) => Task.CompletedTask;
}
// JJQR - END
