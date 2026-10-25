using System.Diagnostics;
using System.Text;

namespace FusionNet.Windows.Network;

/// <summary>Result of a short-lived native command (netsh, ipconfig) used by the network services.</summary>
internal sealed record NetworkCommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Success => !TimedOut && ExitCode == 0;

    /// <summary>stdout + stderr, trimmed, for diagnostics and error messages.</summary>
    public string CombinedOutput
    {
        get
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(StandardOutput)) parts.Add(StandardOutput.Trim());
            if (!string.IsNullOrWhiteSpace(StandardError)) parts.Add(StandardError.Trim());
            return string.Join(Environment.NewLine, parts);
        }
    }
}

/// <summary>
/// Runs console tools without a window, drains stdout/stderr concurrently (so a chatty tool can
/// never deadlock on a full pipe), enforces a timeout and decodes output robustly:
/// strict UTF-8 first, then the OEM console code page, then Latin-1 as a byte-preserving fallback.
/// </summary>
internal static class NetworkCommandRunner
{
    private static readonly Lazy<Encoding?> OemEncoding = new(CreateOemEncoding);

    public static NetworkCommandResult Run(string executable, IEnumerable<string> arguments, int timeoutMs = 4000, CancellationToken cancellationToken = default) =>
        RunAsync(executable, arguments, timeoutMs, cancellationToken).GetAwaiter().GetResult();

    public static async Task<NetworkCommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        int timeoutMs = 4000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return new NetworkCommandResult(-1, string.Empty, "No executable was supplied.", false);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };

        foreach (var argument in arguments)
        {
            if (argument is null) continue;
            startInfo.ArgumentList.Add(argument);
        }
        if (Path.GetFileName(executable).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            startInfo.Environment["PSModulePath"] = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules");

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return new NetworkCommandResult(-1, string.Empty, $"Failed to start {executable}.", false);

            // Start draining immediately: native tools can write more than a pipe buffer.
            var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
            var stderrTask = ReadAllBytesAsync(process.StandardError.BaseStream);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(500, timeoutMs));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                timedOut = true;
                TryKill(process);
            }

            var standardOutput = DecodeBytes(await WaitForDrainAsync(stdoutTask).ConfigureAwait(false));
            var standardError = DecodeBytes(await WaitForDrainAsync(stderrTask).ConfigureAwait(false));

            var exitCode = timedOut ? -1 : SafeExitCode(process);
            return new NetworkCommandResult(exitCode, standardOutput, standardError, timedOut);
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled: never leave a native tool running behind the report.
            if (process is not null) TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            // Win32Exception (file not found / access denied), InvalidOperationException, ...
            return new NetworkCommandResult(-1, string.Empty, $"{executable}: {ex.Message}", false);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>Resolves the full path of a system32 tool, falling back to its bare name.</summary>
    public static string SystemTool(string fileName)
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, fileName);
            return File.Exists(path) ? path : fileName;
        }
        catch (Exception)
        {
            // Environment.SystemDirectory is always available on Windows; be defensive anyway.
            return fileName;
        }
    }

    /// <summary>Collapses native output into a single-line, length-limited message for OperationResult details.</summary>
    public static string Summarize(string? text, int maxLength = 400)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summary = string.Join(" ", lines.Distinct(StringComparer.OrdinalIgnoreCase));
        return summary.Length <= maxLength ? summary : summary[..maxLength] + "…";
    }

    private static async Task<byte[]> WaitForDrainAsync(Task<byte[]> readTask)
    {
        try
        {
            return await readTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The child was killed mid-write; return whatever we captured (possibly nothing).
            return readTask.IsCompletedSuccessfully ? readTask.Result : [];
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 4 * 1024 * 1024)
                    throw new InvalidDataException("Command output exceeded 4 MiB.");
                buffer.Write(chunk, 0, count);
            }
            return buffer.ToArray();
        }
        catch (Exception)
        {
            // Pipe closed abruptly after a kill — treat as empty output.
            return [];
        }
    }

    private static string DecodeBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        try
        {
            // Strict UTF-8: throws when the bytes are not valid UTF-8, which is how netsh output
            // encoded with the OEM code page is detected.
            var strictUtf8 = new UTF8Encoding(false, true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8, fall through to the console code page.
        }

        var oem = OemEncoding.Value;
        if (oem is not null)
        {
            try
            {
                return oem.GetString(bytes);
            }
            catch (Exception)
            {
                // Fall through to Latin-1.
            }
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static Encoding? CreateOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(NetworkInterop.GetOemCodePage());
        }
        catch (Exception)
        {
            // Code page provider missing on a trimmed runtime — strict UTF-8/Latin-1 still work.
            return null;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Process already exited or cannot be killed — nothing else to do.
        }
    }
}
