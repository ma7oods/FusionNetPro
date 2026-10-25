using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using FusionNet.Core.Abstractions;

namespace FusionNet.Windows.System;

/// <summary>
/// Knows whether the process is elevated and can relaunch it through the UAC prompt with
/// the <c>--elevated</c> marker. The app itself always starts <c>asInvoker</c>.
/// </summary>
public sealed class ElevationService : IElevationService
{
    public const string ElevatedArgument = "--elevated";

    // ERROR_CANCELLED: the user dismissed the UAC prompt.
    private const int ErrorCancelled = 1223;

    public bool IsElevated { get; } = CheckIsElevated();

    // The elevated copy waits only a few seconds for this instance's single-instance mutex.
    private readonly Action? _restarted;

    public ElevationService(Action? restarted = null) => _restarted = restarted;

    public bool TryRestartElevated(string? arguments = null)
    {
        if (IsElevated) return false;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };

            foreach (var argument in BuildArguments(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null) return false;
            _restarted?.Invoke();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user cancelled the UAC prompt on purpose; not an error.
            return false;
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine("Elevated restart failed: " + ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            Debug.WriteLine("Elevated restart failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>True when the current Windows identity belongs to the local Administrators group.</summary>
    public static bool CheckIsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
                                       or ArgumentException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>Splits an existing argument line and guarantees exactly one <c>--elevated</c> marker.</summary>
    private static IReadOnlyList<string> BuildArguments(string? arguments)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            result.AddRange(Tokenize(arguments));
        }

        result.RemoveAll(argument => string.Equals(argument, ElevatedArgument, StringComparison.OrdinalIgnoreCase));
        result.Add(ElevatedArgument);
        return result;
    }

    private static IEnumerable<string> Tokenize(string commandLine)
    {
        // The app owns its own command line format (no quotes are used for app switches),
        // so a whitespace split is enough and avoids corrupting paths.
        foreach (var part in commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }
}
