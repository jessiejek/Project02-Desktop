// JJQR - AI (Nov 21, 2026) - unique temp copies of the sandbox DB; these tests never write the original
// and do not run in parallel (VACUUM/backup of the same source can hang or flake in the full suite).
using Microsoft.Data.Sqlite;

namespace ClinicNode.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SandboxCopyCollection : ICollectionFixture<object>
{
    public const string Name = "SandboxCopy";

    public const string DefaultSandboxDir =
        "/Users/jessiejayrubi/Library/Containers/ph.clinicnode.app/Data/Library/ClinicNode";

    /// <summary>
    /// Prefer CLINIC_COPY_DIR (a /tmp copy the suite may opt into). Otherwise the live sandbox, opened
    /// read-only with a short timeout. Never points a test host at the original file.
    /// </summary>
    public static bool TryCopy(out string destDir, out string skipReason)
    {
        destDir = Path.Combine(Path.GetTempPath(), "clinic-node-tests", "sandbox-" + Guid.NewGuid().ToString("N"));
        skipReason = "";

        string? src = null;
        var env = Environment.GetEnvironmentVariable("CLINIC_COPY_DIR") ?? "";
        if (env.Length > 0)
        {
            var envDb = File.Exists(env) ? env : Path.Combine(env, "clinic-node.db");
            if (File.Exists(envDb)) src = envDb;
            else
            {
                skipReason = "CLINIC_COPY_DIR is set but clinic-node.db was not found at " + env;
                return false;
            }
        }
        else
        {
            //JJQR - AI (Nov 24, 2026) - these tests used to copy the clinic's REAL database on their own (patient data in test runs, and demo data pushed into the copy). They now run only when CLINIC_COPY_DIR points at a copy on purpose.
            skipReason = "Set CLINIC_COPY_DIR to a COPY of a clinic-node.db to run this test (it never picks up the live clinic data by itself).";
            return false;
            //JJQR - END
        }

        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, "clinic-node.db");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            CopyReadOnly(src, dest, cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            try { Directory.Delete(destDir, recursive: true); } catch { /* best effort */ }
            destDir = "";
            skipReason = "Could not copy the sandbox DB (it may be locked by a running app): " + ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    static void CopyReadOnly(string src, string dest, CancellationToken ct)
    {
        // Read-only so a running app's WAL lock cannot be taken for writes. BackupDatabase snapshots consistently.
        using var source = new SqliteConnection($"Data Source={src};Mode=ReadOnly;Cache=Shared");
        source.Open();
        ct.ThrowIfCancellationRequested();
        using var target = new SqliteConnection($"Data Source={dest}");
        target.Open();
        source.BackupDatabase(target);
    }
}
// JJQR - END
