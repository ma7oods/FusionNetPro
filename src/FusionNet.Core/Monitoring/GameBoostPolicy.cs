using System.Collections.Immutable;
using FusionNet.Core.Models;

namespace FusionNet.Core.Monitoring;

public sealed record GameBoostPreview(Guid SessionId, ProcessIdentity Owner, DateTimeOffset ExpiresUtc,
    ImmutableArray<ProcessIdentity> Targets, ImmutableArray<string> ProtectedProcessNames,
    ImmutableArray<ProcessIdentity> Games = default)
{
    public string RuleName(int index) => $"FusionNetPro.GB.{SessionId:N}.{index}";
    public string RecoveryTaskName => $"FusionNetPro-GB-{SessionId:N}";
}

public sealed class GameBoostConsent
{
    private GameBoostConsent(GameBoostPreview preview) { Preview = preview; }
    public GameBoostPreview Preview { get; }
    public static GameBoostConsent Approve(GameBoostPreview preview) => new(preview);
}

public sealed record GameBoostStatus(string State, int RuleCount, string Message, DateTimeOffset? StartedUtc = null)
{
    public string DisplayName=>State switch
    {
        "Idle"=>"قراءة فقط", "Starting" or "Preparing"=>"تجهيز الجلسة", "Active"=>"جلسة نشطة",
        "Restoring"=>"جارٍ الاسترجاع", "Restored"=>"استُعيد الاتصال", "Rejected"=>"لم تبدأ الجلسة", _=>"الاسترجاع يحتاج تحققًا"
    };
    public bool IsPending => State is not ("Idle" or "Restored" or "Rejected");
}

public static class GameBoostPolicy
{
    public static IReadOnlyList<ProcessObservation> Recommend(IEnumerable<ProcessObservation> eligible) => eligible
        .Where(process => process.Identity is not null && (process.UploadBytesPerSecond > 0 || process.DownloadBytesPerSecond > 0))
        .GroupBy(process => process.Identity!.Path, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Sum(process => (process.UploadBytesPerSecond ?? 0) + (process.DownloadBytesPerSecond ?? 0)))
        .Take(5).Select(group => group.OrderByDescending(process => (process.UploadBytesPerSecond ?? 0) + (process.DownloadBytesPerSecond ?? 0)).First()).ToArray();
    // A launcher may provide DRM or sign-in for the running game; only Riot titles are known to run without Steam/Epic/etc.
    public static IEnumerable<string> LauncherGuard(IEnumerable<GameProfile> runningGames)
    {
        var games = runningGames.ToArray();
        return games.Length > 0 && games.All(game => game.Id is "valorant" or "league-of-legends") ? []
            : ManagedAppCatalog.All.Where(app => app.Category == ManagedAppCatalog.Launchers).SelectMany(app => app.ProcessNames);
    }
    public static string? BlockReason(ProcessObservation process, IEnumerable<string> extraProtectedNames, string windowsDirectory, string selfPath)
    {
        var identity = process.Identity;
        if (identity is null || identity.Pid <= 4 || string.IsNullOrWhiteSpace(identity.Path)) return "هوية العملية غير متاحة";
        var path = identity.Path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 4 || path[1] != ':' || path[2] != '\\' ||
            path.Split('\\').Any(part => part is ".." or ".") || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "مسار غير محلي أو غير موثوق";
        if (path.StartsWith(windowsDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
            path.Equals(selfPath, StringComparison.OrdinalIgnoreCase)) return "تطبيق النظام أو FusionNet";
        if (GameCatalog.All.Any(game => game.MatchesProcess(process.Name)) || GameCatalog.IsRiotSupportProcess(process.Name) ||
            extraProtectedNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase)) return "لعبة أو مكوّن محمي";
        var app = ManagedAppCatalog.All.FirstOrDefault(app => app.MatchesProcess(process.Name));
        if (app is null || app.Category is not (ManagedAppCatalog.Browsers or ManagedAppCatalog.CloudSync or ManagedAppCatalog.Torrents or ManagedAppCatalog.Downloads or ManagedAppCatalog.Launchers))
            return "دور التطبيق غير مؤكد أو قد تحتاجه اللعبة؛ غير مؤهل للحظر";
        if (!app.Paths.Any(known => Environment.ExpandEnvironmentVariables(known).Equals(path, StringComparison.OrdinalIgnoreCase)))
            return "المسار خارج مواقع التطبيق المعروفة؛ يُرفض بدل افتراض وظيفة الملف من اسمه";
        if (process.Signature != "ValidCached") return "التوقيع لم يُتحقق منه محليًا";
        return null;
    }
}
