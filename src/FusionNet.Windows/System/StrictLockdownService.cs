using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.System;

/// <summary>
/// Temporary safe placeholder for the experimental strict firewall lockdown.
/// The systems engineer replaces this file with the real journaled implementation;
/// every method currently fails closed so no machine can ever be left blocked.
/// </summary>
public sealed class StrictLockdownService : IFirewallLockdownService
{
    public bool IsActive => false;

    public string? ActiveGamePath => null;

    public OperationResult Enable(string? gameExecutablePath, IJournal journal) =>
        OperationResult.Fail("Strict lockdown is not available in this build.");

    public OperationResult Disable(IJournal journal) =>
        OperationResult.Fail("Strict lockdown is not available in this build.");

    public OperationResult RestoreIfActive(IJournal journal) => OperationResult.Ok("Nothing to restore.");
}
