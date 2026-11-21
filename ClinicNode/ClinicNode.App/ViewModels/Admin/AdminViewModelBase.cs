// JJQR - AI (Nov 21, 2026) - shared INPC + busy/error helpers for admin screens.
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;

namespace ClinicNode.App.ViewModels.Admin;

public abstract class AdminViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy == value) return; _isBusy = value; Raise(); Raise(nameof(IsNotBusy)); }
    }
    public bool IsNotBusy => !IsBusy;

    string? _error;
    public string? Error
    {
        get => _error;
        set { if (_error == value) return; _error = value; Raise(); Raise(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    string? _status;
    public string? StatusMessage
    {
        get => _status;
        set { if (_status == value) return; _status = value; Raise(); Raise(nameof(HasStatus)); }
    }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    protected async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        try { await work(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    public static string Peso(decimal value) =>
        "₱" + value.ToString("#,0.##", CultureInfo.GetCultureInfo("en-PH"));

    public static string ClinicDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("ddd, MMM d, yyyy", CultureInfo.GetCultureInfo("en-PH"));

    public static string ClinicDateShort(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-PH"));

    public static string ClinicDateTime(DateTimeOffset value) =>
        value.ToOffset(ClinicClock.Offset).ToString("MMM d, yyyy, h:mm tt", CultureInfo.GetCultureInfo("en-PH"));

    public static string Clock(TimeOnly time) =>
        time.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-PH"));

    public static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    public static string StatusText(string status) => ClinicTheme.StatusLabel(status);

    public static string StatusText(Enum value) => ClinicTheme.StatusLabel(value.ToString());

    //JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
    protected static async Task GoAsync(string route)
    {
        try { await Shell.Current.GoToAsync(route); }
        catch (Exception ex)
        {
            if (Shell.Current is Page page)
                await page.DisplayAlertAsync("Navigation", ex.Message + "\n\nAsk allen to register this route in AppShell / MauiProgram.", "OK");
        }
    }
}
// JJQR - END
