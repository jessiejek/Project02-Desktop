// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;

namespace ClinicNode.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Demo_doctor_can_log_in_against_local_hashes()
    {
        await using var host = await TestHost.StartAsync();
        var auth = host.Get<IAuthService>();
        var result = await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, rememberEmail: true);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(UserRole.Doctor, result.Session!.Role);
        Assert.NotNull(result.Session.DoctorId);
        Assert.Equal("doctor@clinic.test", auth.RememberedEmail);
        Assert.Equal("doctor@clinic.test", await host.Get<IEmailMemory>().GetAsync());
    }

    [Fact]
    public async Task Remember_email_stores_only_the_email()
    {
        await using var host = await TestHost.StartAsync();
        var auth = host.Get<IAuthService>();
        await auth.LoginAsync("staff@clinic.test", DevDataSeeder.DevPassword, rememberEmail: true);
        var stored = await File.ReadAllTextAsync(Path.Combine(host.DataDirectory, "remembered-email.txt"));
        Assert.Equal("staff@clinic.test", stored.Trim());
        Assert.DoesNotContain(DevDataSeeder.DevPassword, stored);
    }

    [Fact]
    public async Task Patient_accounts_cannot_use_the_clinic_node()
    {
        await using var host = await TestHost.StartAsync();
        var result = await host.Get<IAuthService>().LoginAsync("patient@clinic.test", DevDataSeeder.DevPassword, false);
        Assert.False(result.Succeeded);
        Assert.Contains("patient portal", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        var result = await host.Get<IAuthService>().LoginAsync("doctor@clinic.test", "not-the-password", false);
        Assert.False(result.Succeeded);
    }
}
// JJQR - END
