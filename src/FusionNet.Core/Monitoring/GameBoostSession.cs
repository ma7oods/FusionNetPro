using FusionNet.Core.Abstractions;

namespace FusionNet.Core.Monitoring;

public sealed class GameBoostSession(IGameBoostBackend backend, Func<bool> legacySessionPending, SemaphoreSlim? sessionStartGate = null) : IGameBoostSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _sessionId;
    private bool _recoveryChecked;
    public GameBoostStatus Status { get; private set; } = new("Idle", 0, "قراءة فقط؛ لا توجد جلسة حظر.");

    public async Task<GameBoostStatus> StartAsync(GameBoostPreview preview, GameBoostConsent? consent, CancellationToken token)
    {
        if (sessionStartGate is not null) await sessionStartGate.WaitAsync(token).ConfigureAwait(false);
        try { return await StartCoreAsync(preview, consent, token).ConfigureAwait(false); }
        finally { sessionStartGate?.Release(); }
    }

    private async Task<GameBoostStatus> StartCoreAsync(GameBoostPreview preview, GameBoostConsent? consent, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (consent is null || !ReferenceEquals(consent.Preview, preview))
                throw new InvalidOperationException("الموافقة لا تطابق المعاينة الحالية.");
            if (Status.IsPending || legacySessionPending()) throw new InvalidOperationException("أوقف الجلسة السابقة واستعد تغييراتها أولًا.");
            try
            {
                if (await backend.FindPendingAsync(token).ConfigureAwait(false) is { } previous)
                {
                    _sessionId = previous;
                    Status = await backend.ReadAsync(previous, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Status = new("Pending", -1, "تعذر التحقق من سجل الاسترجاع السابق."); }
            if (Status.IsPending) throw new InvalidOperationException("سجل الاسترجاع السابق غير محسوم؛ أوقف واسترجع أولًا.");
            if (preview.SessionId == Guid.Empty || preview.Targets.IsDefaultOrEmpty || preview.Targets.Length > 10 ||
                preview.ExpiresUtc <= DateTimeOffset.UtcNow || preview.ExpiresUtc > DateTimeOffset.UtcNow.AddHours(6))
                throw new InvalidOperationException("أهداف أو مدة الجلسة غير صالحة.");
            _sessionId = preview.SessionId;
            Status = new("Starting", 0, "بانتظار موافقة Windows والتحقق من الأهداف وحفظ الحالة.");
            try { await backend.LaunchAsync(preview, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or global::System.ComponentModel.Win32Exception)
            {
                _sessionId = null;
                Status = new("Rejected", 0, "لم يبدأ مساعد الصلاحيات؛ لا قواعد جديدة.");
            }
            return Status;
        }
        finally { _gate.Release(); }
    }

    public async Task<GameBoostStatus> RefreshAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_recoveryChecked)
            {
                try { _sessionId ??= await backend.FindPendingAsync(token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { return Status = new("Pending", -1, "تعذر التحقق من سجل الاسترجاع السابق؛ لا يمكن بدء تغييرات جديدة بأمان."); }
                _recoveryChecked = true;
            }
            if (_sessionId is { } id) Status = await backend.ReadAsync(id, token).ConfigureAwait(false);
            return Status;
        }
        finally { _gate.Release(); }
    }

    public async Task<GameBoostStatus> StopAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_sessionId is { } id)
            {
                await backend.RequestStopAsync(id, token).ConfigureAwait(false);
                Status = new("Restoring", Status.RuleCount, "طُلب الاسترجاع؛ لا يُعتبر مكتملًا حتى التحقق من زوال القواعد.", Status.StartedUtc);
            }
            return Status;
        }
        finally { _gate.Release(); }
    }
}
