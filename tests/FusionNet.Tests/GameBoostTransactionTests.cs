using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class GameBoostTransactionTests
{
    [Fact] public void DurableStateAndRecoveryAlwaysPrecedeChanges()
    {
        var platform = new Platform();
        Assert.Equal("Active", GameBoostTransaction.Start(platform, 2).State);
        Assert.Equal(new[] { "admin", "validate", "save", "recovery", "validate", "add0", "add1" }, platform.Calls.Take(7));
    }
    [Theory]
    [InlineData("admin")]
    [InlineData("validate")]
    [InlineData("save")]
    [InlineData("recovery")]
    public void PrerequisiteFailureNeverAddsRules(string fail)
    {
        var platform = new Platform { Fail = fail };
        Assert.Throws<InvalidOperationException>(() => GameBoostTransaction.Start(platform, 2));
        Assert.DoesNotContain(platform.Calls, call => call.StartsWith("add"));
    }
    [Fact] public void PartialApplyRollsBackOnlySessionRules()
    {
        var platform = new Platform { Fail = "add1" };
        Assert.Equal("Restored", GameBoostTransaction.Start(platform, 2).State);
        Assert.Empty(platform.Rules);
        Assert.Contains("remove:owned0", platform.Calls);
    }
    [Theory]
    [InlineData("query")]
    [InlineData("remove:owned0")]
    public void FailedQueryOrDeleteNeverConfirmsRestoration(string fail)
    {
        var platform = new Platform { Fail = fail };
        platform.Rules.Add("owned0");
        Assert.True(GameBoostTransaction.Restore(platform).IsPending);
        Assert.DoesNotContain("remove-recovery", platform.Calls);
    }
    [Fact] public void FailedDurableRestorationStatusKeepsRecovery()
    {
        var platform = new Platform { Fail = "status:Restored" };
        Assert.Equal("Pending", GameBoostTransaction.Restore(platform).State);
        Assert.DoesNotContain("remove-recovery", platform.Calls);
    }
    [Fact] public void RuleAbsenceIsDurableBeforeRecoveryIsRemoved()
    {
        var platform = new Platform();
        Assert.Equal("Restored", GameBoostTransaction.Restore(platform).State);
        Assert.True(platform.Calls.IndexOf("status:Restored") < platform.Calls.IndexOf("remove-recovery"));
    }
    [Fact] public void FailedRecoveryTaskCleanupRemainsPending()
    {
        var platform = new Platform { Fail = "remove-recovery" };
        Assert.Equal("Pending", GameBoostTransaction.Restore(platform).State);
        Assert.Equal(new[] { "query", "query", "status:Restored", "remove-recovery", "status:Pending" }, platform.Calls);
    }
    private sealed class Platform : IGameBoostTransactionPlatform
    {
        public string? Fail;
        public List<string> Calls = [];
        public List<string> Rules = [];
        private void Call(string name) { Calls.Add(name); if (name == Fail) throw new InvalidOperationException(name); }
        public void RequireAdministrator() => Call("admin");
        public void ValidateTargets() => Call("validate");
        public void SaveSnapshot() => Call("save");
        public void InstallRecovery() => Call("recovery");
        public void AddRule(int index) { Call("add" + index); Rules.Add("owned" + index); }
        public IReadOnlyList<string> GetOwnedRules() { Call("query"); return Rules.ToArray(); }
        public void RemoveRule(string name) { Call("remove:" + name); Rules.Remove(name); }
        public void RemoveRecovery() => Call("remove-recovery");
        public void SaveStatus(GameBoostStatus status) => Call("status:" + status.State);
    }
}
