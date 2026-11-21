//JJQR - AI (Nov 21, 2026) - the app service graph must resolve ClinicFormsPrintDocumentFactory (not the Core/Print stub) for IPrintDocumentFactory.
using ClinicNode.Core.Print;
using ClinicNode.Core.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Printing;

public class PrintFactoryRegistrationTests
{
    [Fact]
    public async Task App_service_graph_resolves_ClinicFormsPrintDocumentFactory_as_the_last_IPrintDocumentFactory()
    {
        await using var host = await AppTestHost.StartAsync();
        var all = host.Services.GetServices<IPrintDocumentFactory>().ToList();
        Assert.NotEmpty(all);
        Assert.IsType<ClinicFormsPrintDocumentFactory>(all[^1]);
        Assert.IsType<ClinicFormsPrintDocumentFactory>(host.Services.GetRequiredService<IPrintDocumentFactory>());
        Assert.Contains(all, f => f is PrintDocumentFactory);
    }
}
//JJQR - END
