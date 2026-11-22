namespace ClinicNode.App.Services;

/// <summary>This computer's name. MAUI's DeviceInfo.Name says "iPad" on Mac Catalyst, so use the host name there.</summary>
public static class ComputerName
{
    public static string Get()
    {
        var host = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(host)) return host.Replace(".local", "", StringComparison.OrdinalIgnoreCase);
        var dev = DeviceInfo.Current.Name;
        return string.IsNullOrWhiteSpace(dev) ? "Clinic Node" : dev;
    }
}
