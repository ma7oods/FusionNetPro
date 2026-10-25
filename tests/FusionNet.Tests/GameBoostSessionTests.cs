using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class GameBoostSessionTests
{
    private static GameBoostPreview Preview() => new(Guid.NewGuid(), new(1, DateTimeOffset.UnixEpoch, @"C:\f.exe"),
        DateTimeOffset.UtcNow.AddMinutes(30), [new(42, DateTimeOffset.UnixEpoch, @"C:\app.exe")], []);

    [Fact] public async Task NoConsentOrChangedPreviewNeverLaunches()
    {
        var backend = new Backend();
        var session = new GameBoostSession(backend, () => false);
        var preview = Preview();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(preview, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(preview with { ExpiresUtc = preview.ExpiresUtc.AddMinutes(1) }, GameBoostConsent.Approve(preview), default));
        Assert.Equal(0, backend.Launches);
    }
    [Fact] public async Task LegacySessionPreventsOverlap()
    {
        var backend = new Backend();
        var session = new GameBoostSession(backend, () => true);
        var preview = Preview();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(preview, GameBoostConsent.Approve(preview), default));
        Assert.Equal(0, backend.Launches);
    }
    [Fact] public async Task PermissionFailureNeverAppearsActive()
    {
        var backend = new Backend { Deny = true };
        var session = new GameBoostSession(backend, () => false);
        var preview = Preview();
        Assert.Equal("Rejected", (await session.StartAsync(preview, GameBoostConsent.Approve(preview), default)).State);
        Assert.False(session.Status.IsPending);
    }
    [Fact] public async Task UnverifiedRestoreStaysPending()
    {
        var backend = new Backend();
        var session = new GameBoostSession(backend, () => false);
        var preview = Preview();
        await session.StartAsync(preview, GameBoostConsent.Approve(preview), default);
        await session.StopAsync(default);
        Assert.True(session.Status.IsPending);
        Assert.True((await session.RefreshAsync(default)).IsPending);
        backend.Result = new("Restored", 0, "verified");
        Assert.False((await session.RefreshAsync(default)).IsPending);
    }
    [Fact] public async Task PriorPendingStateIsCheckedBeforeFirstStart()
    {
        var backend = new Backend { PendingId = Guid.NewGuid() };
        var session = new GameBoostSession(backend, () => false);
        var preview = Preview();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(preview, GameBoostConsent.Approve(preview), default));
        Assert.Equal(0, backend.Launches);
        Assert.True(session.Status.IsPending);
    }
    [Fact] public async Task UnreadablePriorStateNeverLaunches()
    {
        var backend = new Backend { FindFailure = true };
        var session = new GameBoostSession(backend, () => false);
        var preview = Preview();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(preview, GameBoostConsent.Approve(preview), default));
        Assert.Equal(0, backend.Launches);
        Assert.True(session.Status.IsPending);
    }
    private sealed class Backend : IGameBoostBackend
    {
        public int Launches;
        public bool Deny;
        public Guid? PendingId;
        public bool FindFailure;
        public GameBoostStatus Result = new("Pending", 1, "query failed");
        public Task LaunchAsync(GameBoostPreview preview, CancellationToken token)
        { if (Deny) throw new UnauthorizedAccessException(); Launches++; return Task.CompletedTask; }
        public Task<GameBoostStatus> ReadAsync(Guid id, CancellationToken token) => Task.FromResult(Result);
        public Task RequestStopAsync(Guid id, CancellationToken token) => Task.CompletedTask;
        public Task<Guid?> FindPendingAsync(CancellationToken token) => FindFailure ? throw new IOException("synthetic") : Task.FromResult(PendingId);
    }
}
