using System.Diagnostics;
using System.Text;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Windows.System;

/// <summary>
/// Thread-safe in-memory activity ring buffer (2000 entries) mirrored to a daily text log.
/// Every subsystem writes here through <see cref="IActivityLog"/>; the UI only reads snapshots.
/// </summary>
public sealed class ActivityLog : IActivityLog
{
    public const int Capacity = 2000;

    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly LinkedList<ActivityEntry> _entries = new();

    public ActivityLog()
    {
        try
        {
            AppPaths.EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must work even when the data folder is unavailable.
            Debug.WriteLine("Activity log directory unavailable: " + ex.Message);
        }
    }

    public event EventHandler<ActivityEntry>? EntryAdded;

    public IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (_gate)
        {
            var result = new ActivityEntry[_entries.Count];
            var index = 0;
            for (var node = _entries.Last; node is not null; node = node.Previous)
            {
                result[index++] = node.Value;
            }

            return result;
        }
    }

    public void Log(LogLevel level, string category, string message, string? detail = null)
    {
        var entry = new ActivityEntry(
            DateTimeOffset.UtcNow,
            level,
            string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            message ?? string.Empty,
            string.IsNullOrWhiteSpace(detail) ? null : detail);

        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > Capacity) _entries.RemoveFirst();
        }

        AppendToFile(entry);
        RaiseEntryAdded(entry);
    }

    public void Info(string category, string message, string? detail = null) => Log(LogLevel.Info, category, message, detail);

    public void Success(string category, string message, string? detail = null) => Log(LogLevel.Success, category, message, detail);

    public void Warning(string category, string message, string? detail = null) => Log(LogLevel.Warning, category, message, detail);

    public void Error(string category, string message, string? detail = null) => Log(LogLevel.Error, category, message, detail);

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public string ExportText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{AppPaths.ProductName} activity log");
        builder.AppendLine($"Machine: {Environment.MachineName}");
        builder.AppendLine($"Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine(new string('-', 78));

        ActivityEntry[] entries;
        lock (_gate)
        {
            entries = _entries.ToArray();
        }

        foreach (var entry in entries)
        {
            builder.AppendLine(Format(entry));
        }

        builder.AppendLine(new string('-', 78));
        builder.AppendLine($"{entries.Length} entr{(entries.Length == 1 ? "y" : "ies")}.");
        return builder.ToString();
    }

    private static string Format(ActivityEntry entry)
    {
        var builder = new StringBuilder(160);
        builder.Append('[').Append(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
        builder.Append('[').Append(LevelText(entry.Level)).Append("] ");
        builder.Append('[').Append(entry.Category).Append("] ");
        builder.Append(entry.Message);
        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            builder.Append(" | ").Append(entry.Detail);
        }

        return builder.ToString();
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Success => "OK",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO"
    };

    private void AppendToFile(ActivityEntry entry)
    {
        try
        {
            lock (_fileGate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                var path = Path.Combine(AppPaths.LogDirectory, $"activity-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, Format(entry) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only: the ring buffer still holds the entry for the UI.
            Debug.WriteLine("Activity log file write failed: " + ex.Message);
        }
    }

    private void RaiseEntryAdded(ActivityEntry entry)
    {
        var handler = EntryAdded;
        if (handler is null) return;

        try
        {
            handler(this, entry);
        }
        catch (Exception ex)
        {
            // A faulty subscriber must never break logging for everyone else.
            Debug.WriteLine("ActivityLog subscriber failed: " + ex);
        }
    }
}
