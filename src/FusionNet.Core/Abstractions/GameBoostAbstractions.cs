using FusionNet.Core.Monitoring;

namespace FusionNet.Core.Abstractions;

public interface IGameBoostBackend
{
    Task LaunchAsync(GameBoostPreview preview, CancellationToken token);
    Task<GameBoostStatus> ReadAsync(Guid sessionId, CancellationToken token);
    Task RequestStopAsync(Guid sessionId, CancellationToken token);
    Task<Guid?> FindPendingAsync(CancellationToken token);
}

public interface IGameBoostSession
{
    GameBoostStatus Status { get; }
    Task<GameBoostStatus> StartAsync(GameBoostPreview preview, GameBoostConsent? consent, CancellationToken token);
    Task<GameBoostStatus> RefreshAsync(CancellationToken token);
    Task<GameBoostStatus> StopAsync(CancellationToken token);
}
