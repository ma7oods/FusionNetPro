using System.Security;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using Microsoft.Win32;

namespace FusionNet.Windows.System;

/// <summary>
/// Manages the per-user <c>HKCU\...\Run</c> entry that starts FusionNet Pro minimised to the
/// tray. User-scope only: no administrator rights are ever required.
/// </summary>
public sealed class AutoStartService : IAutoStartService
{
    public const string ValueName = "FusionNetPro";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string TrayArgument = "--tray";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var value = key?.GetValue(ValueName) as string;
                if (string.IsNullOrWhiteSpace(value)) return false;

                var executable = Environment.ProcessPath;
                return string.IsNullOrWhiteSpace(executable) ||
                       value.Contains(executable, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                return false;
            }
        }
    }

    public string CommandDisplay => BuildCommand() ?? string.Empty;

    public OperationResult SetEnabled(bool enabled)
    {
        var command = BuildCommand();
        if (command is null)
            return OperationResult.Fail("The FusionNet Pro executable path could not be resolved.");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return OperationResult.Fail("The Windows startup registry key is not available for this user.");

            if (enabled)
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return OperationResult.Ok(enabled
                ? "FusionNet Pro will start with Windows."
                : "FusionNet Pro will no longer start with Windows.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            return OperationResult.Fail($"Could not update the Windows startup entry: {ex.Message}");
        }
    }

    private static string? BuildCommand()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return null;
        return $"\"{executable}\" {TrayArgument}";
    }
}
