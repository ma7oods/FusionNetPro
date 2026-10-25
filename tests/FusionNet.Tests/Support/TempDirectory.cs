namespace FusionNet.Tests.Support;

/// <summary>
/// Creates a unique, disposable directory under the system temp folder. Tests that need a
/// real file (JSON round-trips, firewall known-path resolution) use this so they never depend
/// on the machine's installed applications and never leak files between runs.
/// <para>
/// The directory creation is defensive: a stale directory left behind by an earlier run, a
/// locked/ACL-denied temp folder or a sandbox that denies writes to <c>%TEMP%</c> must never
/// turn a unit test into an infrastructure failure. We therefore try a few candidate roots and
/// fall back until one is writable, and clean up old leftovers on a best-effort basis.
/// </para>
/// </summary>
public sealed class TempDirectory : IDisposable
{
    private const string BaseFolderName = "FusionNetPro.Tests";

    private static readonly object CleanupGate = new();
    private static readonly HashSet<string> CleanedRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);

    public TempDirectory()
    {
        Root = CreateRoot();
    }

    /// <summary>Absolute path of the unique directory belonging to this instance.</summary>
    public string Root { get; }

    /// <summary>Creates (or overwrites) a file below <see cref="Root"/> and returns its absolute path.</summary>
    public string CreateFile(string relativePath, string content = "test")
    {
        var full = Combine(relativePath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Combines a relative path with <see cref="Root"/>.</summary>
    public string Combine(params string[] parts) =>
        parts.Aggregate(Root, (current, part) => Path.Combine(current, part));

    public void Dispose()
    {
        DeleteQuietly(Root);
    }

    private static string CreateRoot()
    {
        var failures = new List<Exception>(2);
        foreach (var baseDirectory in CandidateBaseDirectories())
        {
            try
            {
                CleanStaleDirectories(baseDirectory);
                var root = Path.Combine(baseDirectory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                // A directory can exist while inherited ACLs still deny file creation; prove the
                // directory is writable before handing it to the test.
                var probe = Path.Combine(root, ".write-probe");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                return root;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or System.Security.SecurityException or NotSupportedException)
            {
                // Try the next candidate root instead of failing the whole test run.
                failures.Add(ex);
            }
        }

        throw new InvalidOperationException(
            "Could not create a writable temporary directory for the FusionNet tests.",
            failures.Count > 0 ? failures[0] : null);
    }

    private static IEnumerable<string> CandidateBaseDirectories()
    {
        // The normal location, then a per-process variant (different ACL inheritance), then the
        // test binary folder, which was already writable because the build wrote the assembly there.
        yield return Path.Combine(Path.GetTempPath(), BaseFolderName);
        yield return Path.Combine(Path.GetTempPath(), BaseFolderName + "-" + Environment.ProcessId);
        yield return Path.Combine(AppContext.BaseDirectory, "test-temp");
    }

    /// <summary>Deletes directories left behind by previous runs; never touches fresh ones.</summary>
    private static void CleanStaleDirectories(string baseDirectory)
    {
        lock (CleanupGate)
        {
            if (!CleanedRoots.Add(baseDirectory)) return;
        }

        try
        {
            if (!Directory.Exists(baseDirectory)) return;

            var cutoff = DateTime.UtcNow - StaleAge;
            foreach (var directory in Directory.EnumerateDirectories(baseDirectory))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) > cutoff) continue;
                    DeleteQuietly(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or System.Security.SecurityException)
                {
                    // Another process may be using it; best effort only.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException or NotSupportedException)
        {
            // A non-enumerable temp folder is not fatal: the directory we create may still work.
        }
    }

    private static void DeleteQuietly(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            // Best effort: a locked temp file must never fail a test run.
            try
            {
                if (!Directory.Exists(path)) return;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(path, recursive: true);
            }
            catch (Exception retryEx) when (retryEx is IOException or UnauthorizedAccessException
                                                or System.Security.SecurityException)
            {
                // Give up quietly; the OS will clean the temp folder eventually.
            }
        }
    }
}
