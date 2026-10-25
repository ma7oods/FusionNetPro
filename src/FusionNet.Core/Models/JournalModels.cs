namespace FusionNet.Core.Models;

/// <summary>A single reversible change recorded in the session journal.</summary>
public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public JournalKind Kind { get; set; }
    public string Target { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? AppliedValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset AppliedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool Restored { get; set; }

    public static JournalEntry Create(JournalKind kind, string target, string description, string? previousValue = null, string? appliedValue = null) =>
        new()
        {
            Kind = kind,
            Target = target,
            Description = description,
            PreviousValue = previousValue,
            AppliedValue = appliedValue
        };
}

/// <summary>Everything needed to fully undo a Fusion Mode session, even after a crash.</summary>
public sealed class JournalDocument
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUpdatedUtc { get; set; }
    public bool Active { get; set; }
    public string? GameName { get; set; }
    public List<JournalEntry> Entries { get; set; } = [];
}
