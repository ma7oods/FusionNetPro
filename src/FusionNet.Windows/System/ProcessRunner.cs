using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FusionNet.Windows.System;

/// <summary>Exit code and captured text of a short-lived console tool run (netsh / powercfg / sc).</summary>
internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>Combined output used for diagnostics and localised-output parsing.</summary>
    public string Output => string.IsNullOrEmpty(StandardError)
        ? StandardOutput
        : StandardOutput + Environment.NewLine + StandardError;
}

/// <summary>
/// Runs console utilities with <see cref="ProcessStartInfo.ArgumentList"/> only - never by
/// concatenating shell strings - with a hard timeout and no visible window.
/// </summary>
internal static class ProcessRunner
{
    private static readonly Lazy<Encoding?> OemEncoding = new(CreateOemEncoding);

    [DllImport("kernel32.dll", EntryPoint = "GetOEMCP")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NativeGetOemCodePage();

    public static CommandResult Run(string fileName, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new CommandResult(-1, string.Empty, "Process failed to start: " + fileName, false);
            }

            // Read both pipes concurrently as raw bytes so a chatty tool can never deadlock on a
            // full buffer, and so the encoding can be detected afterwards instead of assuming UTF-8.
            var outputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
            var errorTask = ReadAllBytesAsync(process.StandardError.BaseStream);

            var effectiveTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout;
            if (!process.WaitForExit((int)Math.Clamp(effectiveTimeout.TotalMilliseconds, 100, 120_000)))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // The process already exited between the timeout check and the kill.
                }

                try { process.WaitForExit(1000); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Nothing more to wait for.
                }

                return new CommandResult(-1, Decode(SafeResult(outputTask)), Decode(SafeResult(errorTask)), true);
            }

            var exitCode = process.ExitCode;
            return new CommandResult(exitCode, Decode(SafeResult(outputTask)), Decode(SafeResult(errorTask)), false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return new CommandResult(-1, string.Empty, ex.Message, false);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        try
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch (Exception)
        {
            // The child was killed mid-write or the pipe closed early: treat as empty output.
            return [];
        }
    }

    private static byte[] SafeResult(Task<byte[]> task)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(2)) ? task.Result : [];
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Decodes native tool output: strict UTF-8 first, then the OEM console code page (what
    /// netsh/powercfg actually emit on localised Windows, e.g. Arabic), then Latin-1 as a
    /// byte-preserving fallback. Never throws.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: native tools use the console OEM code page.
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
                // Fall through to the byte-preserving fallback.
            }
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static Encoding? CreateOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = NativeGetOemCodePage();
            return Encoding.GetEncoding(codePage > 0 ? codePage : 437);
        }
        catch (Exception)
        {
            // Code page provider missing on a trimmed runtime — strict UTF-8/Latin-1 still work.
            return null;
        }
    }
}
