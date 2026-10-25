using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class GameBoostPolicyTests
{
    private static ProcessObservation App(string name = "chrome", string? path = null, string signature = "ValidCached") =>
        new(44, name, new(44, DateTimeOffset.UnixEpoch, path ?? Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe")), 0, 1, 0, null, null, "sampled", signature);
    private static string? Reason(ProcessObservation process, params string[] protectedNames) =>
        GameBoostPolicy.BlockReason(process, protectedNames, @"C:\Windows", @"C:\FusionNet\FusionNetPro.exe");

    [Fact] public void KnownSignedOptionalAppCanBePreviewed() => Assert.Null(Reason(App()));
    [Theory]
    [InlineData("VALORANT-Win64-Shipping")]
    [InlineData("RiotClientServices")]
    [InlineData("MsMpEng")]
    [InlineData("svchost")]
    [InlineData("unknownApp")]
    public void GamesSecurityAndUnknownRolesCannotBeBlocked(string name) => Assert.NotNull(Reason(App(name)));
    [Fact] public void CustomGameIsProtected() => Assert.NotNull(Reason(App(), "chrome"));
    [Fact] public void MissingIdentityAndUnsignedAreNotEligible()
    {
        Assert.NotNull(Reason(App() with { Identity = null }));
        Assert.NotNull(Reason(App(signature: "NotSigned")));
    }
    [Theory]
    [InlineData(@"C:\Windows\System32\chrome.exe")]
    [InlineData(@"C:\FusionNet\FusionNetPro.exe")]
    [InlineData(@"\\server\app.exe")]
    [InlineData(@"C:\Unknown\chrome.exe")]
    public void SystemSelfAndNetworkPathsAreProtected(string path) => Assert.NotNull(Reason(App(path: path)));
    private static ProcessObservation Steam() => App("steam", Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Steam\steam.exe"));
    private static GameProfile Game(string id) => GameCatalog.All.First(game => game.Id == id);
    [Fact] public void LauncherDownloadsCanBePausedWhileOnlyRiotGamesRun() =>
        Assert.Null(Reason(Steam(), [.. GameBoostPolicy.LauncherGuard([Game("valorant")])]));
    [Fact] public void LaunchersStayProtectedForOtherOrUnknownGames()
    {
        Assert.NotNull(Reason(Steam(), [.. GameBoostPolicy.LauncherGuard([Game("valorant"), Game("rocket-league")])]));
        Assert.NotNull(Reason(Steam(), [.. GameBoostPolicy.LauncherGuard([])]));
    }
    [Fact] public void PreviewUsesSeparateOwnershipFromLegacyCleanup()
    {
        var preview = new GameBoostPreview(Guid.Empty, App().Identity!, DateTimeOffset.UtcNow.AddHours(1), [App().Identity!], []);
        Assert.StartsWith("FusionNetPro.GB.", preview.RuleName(0));
        Assert.DoesNotContain("FNP_", preview.RuleName(0));
    }
    [Fact] public void RecommendationsRequireMeasuredActivityAndDeduplicateExecutableScope()
    {
        var one = App() with { UploadBytesPerSecond=100, DownloadBytesPerSecond=100 };
        var two = one with { Pid=45, Identity=one.Identity! with { Pid=45 }, DownloadBytesPerSecond=200 };
        var unknown = App("firefox") with { Identity=App().Identity! with { Path=@"C:\other.exe",Pid=47 }, Pid=47 };
        var recommendations=GameBoostPolicy.Recommend([one,two,unknown]);
        Assert.Single(recommendations);
        Assert.Equal(45,recommendations[0].Pid);
    }
}
