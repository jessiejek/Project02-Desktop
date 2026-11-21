// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Admin;

namespace ClinicNode.Tests.Admin;

public class AdminAnnouncementTests
{
    [Fact]
    public async Task Create_requires_title_and_body()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminAnnouncementService>();
        var missing = await svc.CreateAsync(new AnnouncementEdit("", "", true));
        Assert.False(missing.Succeeded);
        Assert.Contains("Title", missing.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Crud_round_trip_and_active_filter()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminAnnouncementService>();

        var created = await svc.CreateAsync(new AnnouncementEdit("Holiday hours", "The clinic closes at noon.", true));
        Assert.True(created.Succeeded, created.Error);
        Assert.Equal("Holiday hours", created.Value!.Title);

        var listed = await svc.ListAsync(activeOnly: true);
        Assert.Contains(listed, a => a.Id == created.Value.Id);

        var updated = await svc.UpdateAsync(created.Value.Id, new AnnouncementEdit("Holiday hours", "Closed all day.", false));
        Assert.True(updated.Succeeded, updated.Error);
        Assert.False(updated.Value!.IsActive);

        var activeOnly = await svc.ListAsync(activeOnly: true);
        Assert.DoesNotContain(activeOnly, a => a.Id == created.Value.Id);

        var deleted = await svc.DeleteAsync(created.Value.Id);
        Assert.True(deleted.Succeeded, deleted.Error);
        var all = await svc.ListAsync(activeOnly: false);
        Assert.DoesNotContain(all, a => a.Id == created.Value.Id);
    }
}
// JJQR - END
