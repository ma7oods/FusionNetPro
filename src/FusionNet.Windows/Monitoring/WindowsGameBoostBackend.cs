using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Monitoring;

namespace FusionNet.Windows.Monitoring;

public sealed class WindowsGameBoostBackend : IGameBoostBackend
{
    private Process? _helper;
    private readonly string _stopDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FusionNetPro", "GameBoostStop");

    public async Task LaunchAsync(GameBoostPreview preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_stopDirectory);
        var normalized = preview with { Games = preview.Games.IsDefault ? [] : preview.Games };
        var request = new GameBoostWorkerRequest(normalized, WindowsIdentity.GetCurrent().User!.Value, StopPath(preview.SessionId));
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        if (data.Length > 24000) throw new InvalidOperationException("اختر عددًا أقل من التطبيقات؛ حجم الطلب أكبر من الحد.");
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "--gameboost-worker=" + data
        };
        _helper?.Dispose();
        _helper = await Task.Run(() => Process.Start(start) ?? throw new InvalidOperationException("Could not start the privileged worker.")).ConfigureAwait(false);
    }

    public Task<GameBoostStatus> ReadAsync(Guid sessionId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FusionNetPro.GameBoost", sessionId.ToString("N"), "status.json");
        try
        {
            if (!File.Exists(path))
                return Task.FromResult(_helper is { HasExited: true }
                    ? new GameBoostStatus("Rejected", 0, "رفض المساعد بدء الجلسة قبل حفظ حالة التغيير؛ راجع الصلاحيات وهوية الأهداف.")
                    : new GameBoostStatus("Starting", 0, "بانتظار التحقق من الهوية وحفظ الحالة وتجهيز الاسترجاع."));
            new ProtectedSessionStorage(sessionId, WindowsIdentity.GetCurrent().User!.Value).ValidateExisting();
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid session status.");
            var status = JsonSerializer.Deserialize<GameBoostStatus>(File.ReadAllText(path)) ?? throw new JsonException();
            if (status.State is not ("Preparing" or "Active" or "Restoring" or "Restored" or "Pending" or "Rejected")) throw new JsonException();
            if (status.State == "Active" && _helper is { HasExited: true })
                status = new("Pending", status.RuleCount, "انتهى المساعد؛ حالة القواعد تحتاج تحققًا. المهمة المستقلة ستسترجعها عند المحاولة التالية.", status.StartedUtc);
            return Task.FromResult(status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return Task.FromResult(new GameBoostStatus("Pending", -1, "حالة الاسترجاع غير متاحة؛ لا يمكن تأكيد زوال القواعد.")); }
    }

    public async Task RequestStopAsync(Guid sessionId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_stopDirectory);
        File.WriteAllText(StopPath(sessionId), "stop");
        if (_helper is null || _helper.HasExited)
        {
            _helper?.Dispose();
            _helper = await Task.Run(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, Arguments = "--gameboost-restore=" + sessionId.ToString("N") }), token).ConfigureAwait(false);
        }
    }
    public async Task<Guid?> FindPendingAsync(CancellationToken token) => await Task.Run(() =>
        FindPending(ProtectedSessionStorage.Root, WindowsIdentity.GetCurrent().User!.Value, token), token).ConfigureAwait(false);

    private static Guid? FindPending(string root, string user, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(root)) return null;
        // Retained completed sessions must not impose a lifetime limit on future sessions.
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            var snapshot = Path.Combine(directory, "snapshot.json");
            try
            {
                if (!File.Exists(snapshot) || new FileInfo(snapshot).Length > 65536) continue;
                var request = JsonSerializer.Deserialize<GameBoostWorkerRequest>(File.ReadAllText(snapshot));
                if (request?.UserSid != user) continue;
                var statusPath = Path.Combine(directory, "status.json");
                if (!File.Exists(statusPath)) return (Guid?)id;
                if (new FileInfo(statusPath).Length > 16384) return (Guid?)id;
                var status = JsonSerializer.Deserialize<GameBoostStatus>(File.ReadAllText(statusPath));
                if (status is null || status.IsPending) return (Guid?)id;
            }
            catch (UnauthorizedAccessException) { /* another Windows user's private session */ }
            catch (JsonException) { return (Guid?)id; }
        }
        return null;
    }
    private string StopPath(Guid id) => Path.Combine(_stopDirectory, id.ToString("N") + ".stop");
}
