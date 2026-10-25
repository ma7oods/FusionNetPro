using System.Diagnostics;
using System.Text.Json;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Windows.System;

/// <summary>
/// The crash-recovery journal: one JSON document at <see cref="AppPaths.JournalFile"/> written
/// atomically after every mutation. <see cref="HasPendingRestore"/> is true when a previous
/// session died before restoring the machine, which the coordinator undoes on next launch.
/// </summary>
public sealed class JournalService : IJournal
{
    public const int MaxEntries = 5000;

    private readonly object _gate = new();
    private JournalDocument _document;

    public JournalService()
    {
        _document = Load();
    }

    public JournalDocument Document
    {
        get
        {
            lock (_gate) return _document;
        }
    }

    public IReadOnlyList<JournalEntry> Entries
    {
        get
        {
            lock (_gate) return _document.Entries.ToArray();
        }
    }

    public bool HasPendingRestore
    {
        get
        {
            lock (_gate) return _document.Active;
        }
    }

    /// <summary>Last load/save problem, for diagnostics only.</summary>
    public string? LastError { get; private set; }

    public void Begin(string? gameName = null)
    {
        lock (_gate)
        {
            _document = new JournalDocument
            {
                SessionId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTimeOffset.UtcNow,
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Active = true,
                GameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim(),
                Entries = []
            };

            SaveLocked();
        }
    }

    public JournalEntry Record(JournalKind kind, string target, string description,
        string? previousValue = null, string? appliedValue = null)
    {
        var entry = JournalEntry.Create(kind, target ?? string.Empty, description ?? string.Empty,
            previousValue, appliedValue);

        lock (_gate)
        {
            _document.Entries.Add(entry);
            if (_document.Entries.Count > MaxEntries)
            {
                // Keep the newest window; the oldest entries have long since been reconciled.
                _document.Entries.RemoveRange(0, _document.Entries.Count - MaxEntries);
            }

            _document.LastUpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
        }

        return entry;
    }

    public void MarkRestored(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return;

        lock (_gate)
        {
            var entry = _document.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, entryId, StringComparison.Ordinal));
            if (entry is null) return;

            entry.Restored = true;
            _document.LastUpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            SaveLocked();
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _document.Active = false;
            _document.LastUpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _document = new JournalDocument
            {
                StartedAtUtc = DateTimeOffset.UtcNow,
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Active = false,
                Entries = []
            };

            SaveLocked();
        }
    }

    private JournalDocument Load()
    {
        var path = AppPaths.JournalFile;
        try
        {
            if (!File.Exists(path)) return NewDocument();
            var json = File.ReadAllText(path);
            var document = FusionJson.Deserialize<JournalDocument>(json);
            if (document is null)
            {
                Quarantine(path);
                return NewDocument();
            }

            document.Entries ??= [];
            document.LastUpdatedUtc ??= document.StartedAtUtc;
            LastError = null;
            return document;
        }
        catch (JsonException ex)
        {
            LastError = "Journal file was corrupted and has been quarantined: " + ex.Message;
            Quarantine(path);
            return NewDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Keep running with an empty in-memory document; the next Save recreates the file.
            LastError = "Could not read the journal: " + ex.Message;
            Debug.WriteLine(LastError);
            return NewDocument();
        }
    }

    private void SaveLocked()
    {
        try
        {
            AppPaths.EnsureDirectories();
            FusionJson.SaveAtomic(AppPaths.JournalFile, _document);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A locked file must never break a Fusion Mode transition; it is retried on the next
            // Record/Save and reported through LastError for diagnostics.
            LastError = "Could not save the journal: " + ex.Message;
            Debug.WriteLine(LastError);
        }
    }

    private static JournalDocument NewDocument() => new()
    {
        StartedAtUtc = DateTimeOffset.UtcNow,
        LastUpdatedUtc = DateTimeOffset.UtcNow,
        Active = false,
        Entries = []
    };

    private static void Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(AppPaths.DataDirectory, $"journal.corrupt-{stamp}.json");
            var suffix = 1;
            while (File.Exists(target))
            {
                target = Path.Combine(AppPaths.DataDirectory, $"journal.corrupt-{stamp}-{suffix++}.json");
            }

            File.Move(path, target, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Journal quarantine failed: " + ex.Message);
        }
    }
}
