// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Admin;

namespace ClinicNode.Tests.Files;

public class AdminLocalExportTests
{
    [Fact]
    public void WriteUtf8_writes_to_a_chosen_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-export-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = AdminLocalExport.WriteUtf8("hello.csv", "a,b\n1,2", dir);
            Assert.Equal(Path.Combine(dir, "hello.csv"), path);
            Assert.True(File.Exists(path));
            Assert.Equal("a,b\n1,2", File.ReadAllText(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
// JJQR - END
