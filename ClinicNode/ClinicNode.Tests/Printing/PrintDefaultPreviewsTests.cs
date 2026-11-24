//JJQR - AI (Nov 24, 2026) - the Admin > Printing tab shows the real built-in layout of each document, never an empty box.
using ClinicNode.App.Views.Admin;
using ClinicNode.Core.Printing;
using Xunit;

namespace ClinicNode.Tests.Printing;

public class PrintDefaultPreviewsTests
{
    [Fact]
    public async Task Every_document_has_a_real_default_preview_picture()
    {
        foreach (var (kind, _, label) in PrintPolicyKinds.All)
        {
            var source = PrintDefaultPreviews.For(kind);
            Assert.True(source is StreamImageSource, $"{label} has a default preview");
            await using var stream = await ((StreamImageSource)source!).Stream(CancellationToken.None);
            var head = new byte[8];
            Assert.Equal(8, await stream.ReadAsync(head));
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, head);   // a PNG picture
        }
        Assert.Null(PrintDefaultPreviews.For(ClinicPrintKind.Other));
    }
}
//JJQR - END
