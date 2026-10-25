using System.Diagnostics;
using System.Security;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using Microsoft.Win32;

namespace FusionNet.Windows.System;

/// <summary>
/// Implemented by <see cref="RegistryTweakService"/> so the coordinator can write the exact
/// previous value recorded in the journal back to the registry after a crash or on disengage.
/// </summary>
public interface ITweakJournalRestorer
{
    /// <summary>
    /// Restores one journaled registry value. <paramref name="interfaceGuid"/> limits a
    /// per-adapter tweak to its own TCP/IP interface sub-key. A null/absent previous value
    /// deletes the value again.
    /// </summary>
    OperationResult RestorePrevious(TweakDefinition tweak, string? previousValue, string? interfaceGuid);
}

/// <summary>
/// Applies and reverses every <see cref="TweakCatalog"/> entry through the registry. Reading is
/// always safe (also without admin); writing machine-wide values reports NotElevated instead of
/// throwing, and each write is journaled with the exact previous value that must be restored.
/// </summary>
public sealed class RegistryTweakService : IRegistryTweakService, ITweakJournalRestorer
{
    private const string Category = "Tweaks";
    private const string Absent = "absent";

    private readonly IActivityLog _activity;

    public RegistryTweakService(IActivityLog activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    // ================================================================= reads

    public TweakState Read(TweakDefinition tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);

        var targets = EnumerateTargets(tweak);
        var appliedTargets = 0;
        var currentValues = new List<string>(targets.Count);
        string? error = null;

        foreach (var target in targets)
        {
            try
            {
                using var key = OpenKey(target, write: false);
                if (key is null)
                {
                    currentValues.Add(Absent);
                    continue;
                }

                if (ValueMatches(key, tweak, out var exists, out _))
                {
                    appliedTargets++;
                }

                var (_, _, serialized) = ReadSerializedValue(key, tweak.ValueName);
                currentValues.Add(exists ? serialized ?? "unsupported" : Absent);
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                error ??= ex.Message;
            }
        }

        var currentValue = targets.Count switch
        {
            0 => Absent,
            1 => currentValues.FirstOrDefault() ?? Absent,
            _ => string.Join(" | ", currentValues.Distinct(StringComparer.Ordinal))
        };

        return new TweakState(
            tweak.Id,
            Applied: targets.Count > 0 && appliedTargets == targets.Count,
            TargetCount: targets.Count,
            AppliedTargetCount: appliedTargets,
            CurrentValue: currentValue,
            ExpectedValue: SerializeExpected(tweak),
            ErrorText: error ?? (targets.Count == 0 && tweak.Scope == TweakScope.PerInterface
                ? "No active network adapter was detected."
                : null));
    }

    public IReadOnlyList<TweakState> ReadAll(IEnumerable<TweakDefinition> tweaks)
    {
        if (tweaks is null) return [];
        return tweaks.Where(tweak => tweak is not null).Select(Read).ToArray();
    }

    // ================================================================= writes

    public OperationResult Apply(TweakDefinition tweak, IJournal journal)
    {
        ArgumentNullException.ThrowIfNull(tweak);

        if (!CanWrite(tweak.Scope, $"applying {tweak.ValueName}")) return NotElevated(tweak);

        var targets = EnumerateTargets(tweak);
        if (targets.Count == 0)
        {
            var message = tweak.Scope == TweakScope.PerInterface
                ? "No active network adapter was found; nothing was written."
                : $"Registry key for {tweak.Id} was not found.";
            _activity.Warning(Category, message);
            return OperationResult.Fail(message);
        }

        var applied = 0;
        var errors = new List<string>();
        var expected = SerializeExpected(tweak);

        foreach (var target in targets)
        {
            try
            {
                using var key = OpenKey(target, write: true);
                if (key is null)
                {
                    errors.Add($"{target.Label}: registry key is missing");
                    continue;
                }

                if (ValueMatches(key, tweak, out _, out _))
                {
                    applied++;
                    continue;
                }

                var (exists, supported, previous) = ReadSerializedValue(key, tweak.ValueName);
                if (exists && !supported)
                {
                    errors.Add($"{target.Label}: value has a type FusionNet cannot safely restore");
                    continue;
                }

                var entry = Record(journal, tweak, target, previous ?? Absent, expected);
                if (!TryWrite(key, tweak, out var writeError))
                {
                    errors.Add($"{target.Label}: {writeError}");
                    MarkRestored(journal, entry);
                    continue;
                }

                applied++;
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                errors.Add(DescribeTarget(target) + ex.Message);
            }
        }

        if (errors.Count == 0)
        {
            _activity.Success(Category, $"Applied {tweak.Id} ({tweak.ValueName}) on {applied} target(s).");
            return OperationResult.Ok($"Applied {tweak.Id} on {applied} target(s).");
        }

        _activity.Warning(Category, $"Applying {tweak.Id} was incomplete: {string.Join("; ", errors)}");
        return OperationResult.Fail($"Could not apply {tweak.Id} completely.", errors);
    }

    public OperationResult Revert(TweakDefinition tweak, IJournal journal)
    {
        ArgumentNullException.ThrowIfNull(tweak);

        if (!CanWrite(tweak.Scope, $"reverting {tweak.ValueName}")) return NotElevated(tweak);

        var targets = EnumerateTargets(tweak);
        if (targets.Count == 0) return OperationResult.Ok($"{tweak.Id}: no target key was found; nothing to revert.");

        var reverted = 0;
        var errors = new List<string>();

        foreach (var target in targets)
        {
            try
            {
                using var key = OpenKey(target, write: true);
                if (key is null)
                {
                    reverted++;
                    continue;
                }

                var (exists, supported, previous) = ReadSerializedValue(key, tweak.ValueName);
                if (!exists)
                {
                    reverted++;
                    continue;
                }

                if (!supported)
                {
                    errors.Add($"{target.Label}: value has a type FusionNet cannot safely restore");
                    continue;
                }

                var entry = Record(journal, tweak, target, previous ?? Absent, Absent);
                key.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
                reverted++;
                if (entry is not null) JournalMarkRestored(journal, entry.Id);
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                errors.Add(DescribeTarget(target) + ex.Message);
            }
        }

        if (errors.Count == 0)
        {
            _activity.Info(Category, $"Reverted {tweak.Id} on {reverted} target(s).");
            return OperationResult.Ok($"Reverted {tweak.Id}.");
        }

        _activity.Warning(Category, $"Reverting {tweak.Id} was incomplete: {string.Join("; ", errors)}");
        return OperationResult.Fail($"Could not revert {tweak.Id} completely.", errors);
    }

    public OperationResult ApplyAll(IEnumerable<TweakDefinition> tweaks, IJournal journal)
    {
        if (tweaks is null) return OperationResult.Ok("No tweaks were selected.");

        var applied = 0;
        var failures = new List<string>();
        var firstFailure = string.Empty;

        foreach (var tweak in tweaks.Where(tweak => tweak is not null))
        {
            OperationResult result;
            try
            {
                result = Apply(tweak, journal);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or SecurityException)
            {
                result = OperationResult.Fail($"Unexpected error applying {tweak.Id}: {ex.Message}");
            }

            if (result.Success) applied++;
            else
            {
                if (firstFailure.Length == 0) firstFailure = result.Message;
                failures.Add($"{tweak.Id}: {result.Message}");
            }
        }

        return failures.Count == 0
            ? OperationResult.Ok($"Applied {applied} latency tweaks.")
            : OperationResult.Fail($"Applied {applied} tweaks, {failures.Count} failed. {firstFailure}", failures);
    }

    public OperationResult RevertAll(IEnumerable<TweakDefinition> tweaks, IJournal journal)
    {
        if (tweaks is null) return OperationResult.Ok("No tweaks were selected.");

        var reverted = 0;
        var failures = new List<string>();
        var firstFailure = string.Empty;

        foreach (var tweak in tweaks.Where(tweak => tweak is not null))
        {
            OperationResult result;
            try
            {
                result = Revert(tweak, journal);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or SecurityException)
            {
                result = OperationResult.Fail($"Unexpected error reverting {tweak.Id}: {ex.Message}");
            }

            if (result.Success) reverted++;
            else
            {
                if (firstFailure.Length == 0) firstFailure = result.Message;
                failures.Add($"{tweak.Id}: {result.Message}");
            }
        }

        return failures.Count == 0
            ? OperationResult.Ok($"Reverted {reverted} latency tweaks.")
            : OperationResult.Fail($"Reverted {reverted} tweaks, {failures.Count} failed. {firstFailure}", failures);
    }

    // ================================================================= crash recovery

    public OperationResult RestorePrevious(TweakDefinition tweak, string? previousValue, string? interfaceGuid)
    {
        ArgumentNullException.ThrowIfNull(tweak);

        if (!CanWrite(tweak.Scope, $"restoring {tweak.ValueName}")) return NotElevated(tweak);

        IReadOnlyList<RegistryTarget> targets;
        if (tweak.Scope == TweakScope.PerInterface)
        {
            if (string.IsNullOrWhiteSpace(interfaceGuid))
            {
                // No interface captured in the journal: restoring the whole scope would risk
                // writing into adapters that were never touched, so report it instead.
                return OperationResult.Fail($"Cannot restore {tweak.Id} without its network adapter identifier.");
            }

            targets = [new RegistryTarget(interfaceGuid, tweak.RegistryPath + "\\" + interfaceGuid, IsMachine: true)];
        }
        else
        {
            targets = EnumerateTargets(tweak);
        }

        var restored = 0;
        var errors = new List<string>();
        var delete = string.IsNullOrWhiteSpace(previousValue) ||
                     string.Equals(previousValue, Absent, StringComparison.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            try
            {
                using var key = OpenKey(target, write: true);
                if (key is null) continue;

                if (delete)
                {
                    key.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
                }
                else if (!TryWriteSerialized(key, tweak.ValueName, previousValue!, out var error))
                {
                    errors.Add($"{target.Label}: {error}");
                    continue;
                }

                restored++;
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                errors.Add(DescribeTarget(target) + ex.Message);
            }
        }

        if (errors.Count > 0)
        {
            _activity.Warning(Category, $"Could not restore {tweak.Id}: {string.Join("; ", errors)}");
            return OperationResult.Fail($"Could not restore {tweak.Id}.", errors);
        }

        _activity.Info(Category, $"Restored {tweak.Id} previous value ({previousValue ?? Absent}).");
        return OperationResult.Ok($"Restored {tweak.Id}.");
    }

    // ================================================================= registry plumbing

    private readonly record struct RegistryTarget(string Label, string SubKeyPath, bool IsMachine);

    private static IReadOnlyList<RegistryTarget> EnumerateTargets(TweakDefinition tweak)
    {
        if (tweak.Scope != TweakScope.PerInterface)
        {
            return [new RegistryTarget(tweak.Scope == TweakScope.Machine ? "machine" : "user", tweak.RegistryPath,
                tweak.Scope == TweakScope.Machine)];
        }

        var targets = new List<RegistryTarget>();
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(tweak.RegistryPath, writable: false);
            if (interfaces is null) return targets;

            foreach (var name in interfaces.GetSubKeyNames())
            {
                try
                {
                    using var subKey = interfaces.OpenSubKey(name, writable: false);
                    if (subKey is null) continue;
                    if (!HasIpConfiguration(subKey)) continue;
                    targets.Add(new RegistryTarget(name, tweak.RegistryPath + "\\" + name, IsMachine: true));
                }
                catch (Exception ex) when (IsRegistryFailure(ex))
                {
                    // A single protected interface key must not abort the whole enumeration.
                }
            }
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // Registry access unavailable: caller reports "no active adapter" instead of crashing.
        }

        return targets;
    }

    private static bool HasIpConfiguration(RegistryKey interfaceKey)
    {
        foreach (var name in new[] { "IPAddress", "DhcpIPAddress", "DhcpServer", "DefaultGateway", "NameServer", "Domain" })
        {
            if (interfaceKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
                return true;
        }

        // DHCP clients always carry EnableDHCP even before they get a lease.
        return interfaceKey.GetValue("EnableDHCP", null) is not null;
    }

    private static RegistryKey? OpenKey(RegistryTarget target, bool write)
    {
        var baseKey = target.IsMachine ? Registry.LocalMachine : Registry.CurrentUser;
        if (!write) return baseKey.OpenSubKey(target.SubKeyPath, writable: false);
        return baseKey.OpenSubKey(target.SubKeyPath, writable: true) ?? baseKey.CreateSubKey(target.SubKeyPath, writable: true);
    }

    /// <summary>Reads the current value and serialises it in the TEAM_SPEC journal format.</summary>
    private static (bool Exists, bool Supported, string? Serialized) ReadSerializedValue(RegistryKey key, string valueName)
    {
        var raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return (false, true, null);

        var kind = key.GetValueKind(valueName);
        return kind switch
        {
            RegistryValueKind.DWord => (true, true, "dword:" + unchecked((uint)Convert.ToInt32(raw))),
            RegistryValueKind.QWord => (true, true, "qword:" + unchecked((ulong)Convert.ToInt64(raw))),
            RegistryValueKind.String => (true, true, "string:" + Convert.ToString(raw)),
            RegistryValueKind.ExpandString => (true, true, "expand:" + Convert.ToString(raw)),
            _ => (true, false, null)
        };
    }

    private static bool ValueMatches(RegistryKey key, TweakDefinition tweak, out bool exists, out bool supported)
    {
        supported = true;
        var raw = key.GetValue(tweak.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        exists = raw is not null;
        if (raw is null) return false;

        var actualKind = key.GetValueKind(tweak.ValueName);
        switch (tweak.Kind)
        {
            case TweakValueKind.Dword:
                return actualKind == RegistryValueKind.DWord && raw is int dword &&
                       unchecked((uint)dword) == unchecked((uint)Convert.ToInt64(tweak.Value));

            case TweakValueKind.Qword:
                return actualKind == RegistryValueKind.QWord && raw is long qword &&
                       qword == Convert.ToInt64(tweak.Value);

            case TweakValueKind.String:
                return actualKind == RegistryValueKind.String && raw is string text &&
                       string.Equals(text, Convert.ToString(tweak.Value), StringComparison.Ordinal);

            case TweakValueKind.ExpandString:
                return actualKind == RegistryValueKind.ExpandString && raw is string expanded &&
                       string.Equals(expanded, Convert.ToString(tweak.Value), StringComparison.Ordinal);

            default:
                supported = false;
                return false;
        }
    }

    private static bool TryWrite(RegistryKey key, TweakDefinition tweak, out string? error)
    {
        try
        {
            switch (tweak.Kind)
            {
                case TweakValueKind.Dword:
                    key.SetValue(tweak.ValueName, unchecked((int)Convert.ToInt64(tweak.Value)), RegistryValueKind.DWord);
                    break;
                case TweakValueKind.Qword:
                    key.SetValue(tweak.ValueName, Convert.ToInt64(tweak.Value), RegistryValueKind.QWord);
                    break;
                case TweakValueKind.String:
                    key.SetValue(tweak.ValueName, Convert.ToString(tweak.Value) ?? string.Empty, RegistryValueKind.String);
                    break;
                case TweakValueKind.ExpandString:
                    key.SetValue(tweak.ValueName, Convert.ToString(tweak.Value) ?? string.Empty, RegistryValueKind.ExpandString);
                    break;
                default:
                    error = "unsupported value kind";
                    return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryWriteSerialized(RegistryKey key, string valueName, string serialized, out string? error)
    {
        error = null;
        var separator = serialized.IndexOf(':');
        var kind = separator < 0 ? "string" : serialized[..separator];
        var value = separator < 0 ? serialized : serialized[(separator + 1)..];

        try
        {
            switch (kind.ToLowerInvariant())
            {
                case "dword":
                    if (!uint.TryParse(value, out var dword))
                    {
                        error = "invalid dword value";
                        return false;
                    }

                    key.SetValue(valueName, unchecked((int)dword), RegistryValueKind.DWord);
                    return true;

                case "qword":
                    if (!ulong.TryParse(value, out var qword))
                    {
                        error = "invalid qword value";
                        return false;
                    }

                    key.SetValue(valueName, unchecked((long)qword), RegistryValueKind.QWord);
                    return true;

                case "string":
                    key.SetValue(valueName, value, RegistryValueKind.String);
                    return true;

                case "expand":
                    key.SetValue(valueName, value, RegistryValueKind.ExpandString);
                    return true;

                default:
                    error = "unknown serialised value type";
                    return false;
            }
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    private static string SerializeExpected(TweakDefinition tweak) => tweak.Kind switch
    {
        TweakValueKind.Dword => "dword:" + unchecked((uint)Convert.ToInt64(tweak.Value)),
        TweakValueKind.Qword => "qword:" + unchecked((ulong)Convert.ToInt64(tweak.Value)),
        TweakValueKind.String => "string:" + Convert.ToString(tweak.Value),
        TweakValueKind.ExpandString => "expand:" + Convert.ToString(tweak.Value),
        _ => Absent
    };

    private static string DescribeTarget(RegistryTarget target) => target.IsMachine ? $"HKLM\\{target.SubKeyPath}: " : $"HKCU\\{target.SubKeyPath}: ";

    private static bool IsRegistryFailure(Exception exception) =>
        exception is UnauthorizedAccessException or SecurityException or IOException or
            ArgumentException or NotSupportedException or InvalidOperationException or ObjectDisposedException;

    // ================================================================= journal helpers

    private static JournalEntry? Record(IJournal? journal, TweakDefinition tweak, RegistryTarget target,
        string previousValue, string appliedValue)
    {
        if (journal is null) return null;

        // TEAM_SPEC target format is "tweak:<id>". Per-adapter tweaks append "|if:<guid>" so the
        // reverse pass can write each adapter's own previous value back exactly.
        var journalTarget = tweak.Scope == TweakScope.PerInterface
            ? $"tweak:{tweak.Id}|if:{target.Label}"
            : $"tweak:{tweak.Id}";

        try
        {
            return journal.Record(JournalKind.Registry, journalTarget,
                $"{(previousValue == appliedValue ? "Revert" : "Apply")} {tweak.ValueName} ({tweak.Id})",
                previousValue, appliedValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A failed journal write must not undo a registry change that already succeeded;
            // the coordinator reports the pending-restore state on the next startup.
            Debug.WriteLine($"Journal record for {tweak.Id} failed: {ex.Message}");
            return null;
        }
    }

    private static void MarkRestored(IJournal? journal, JournalEntry? entry)
    {
        if (journal is null || entry is null) return;
        try { journal.MarkRestored(entry.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine("Journal mark-restored failed: " + ex.Message);
        }
    }

    private static void JournalMarkRestored(IJournal journal, string entryId)
    {
        try { journal.MarkRestored(entryId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine("Journal mark-restored failed: " + ex.Message);
        }
    }

    private bool CanWrite(TweakScope scope, string action) =>
        scope == TweakScope.CurrentUser || ElevationService.CheckIsElevated() || LogAndReturnFalse(action);

    private bool LogAndReturnFalse(string action)
    {
        _activity.Warning(Category, $"{action} needs administrator rights.");
        return false;
    }

    private static OperationResult NotElevated(TweakDefinition tweak) =>
        OperationResult.NotElevated($"Changing the {tweak.ValueName} registry value");
}
