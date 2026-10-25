using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.System;

/// <summary>
/// A do-nothing journal used for user-initiated tuner changes while Fusion Mode is idle.
/// Those changes are meant to persist across restarts, so they must not become a pending
/// crash-restore document that would silently undo the user's choices on next launch.
/// </summary>
internal sealed class NullJournal : IJournal
{
    public static NullJournal Instance { get; } = new();

    private readonly JournalDocument _document = new() { Active = false };

    private NullJournal()
    {
    }

    public JournalDocument Document => _document;

    public IReadOnlyList<JournalEntry> Entries => [];

    public bool HasPendingRestore => false;

    public void Begin(string? gameName = null) { }

    public JournalEntry Record(JournalKind kind, string target, string description,
        string? previousValue = null, string? appliedValue = null) =>
        JournalEntry.Create(kind, target, description, previousValue, appliedValue);

    public void MarkRestored(string entryId) { }

    public void Save() { }

    public void Complete() { }

    public void Clear() { }
}
