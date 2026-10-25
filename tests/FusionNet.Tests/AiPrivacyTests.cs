using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class AiPrivacyTests
{
    [Fact] public void PayloadDropsNamesPathsAddressesAndRawCommands()
    {
        var report = new LocalMonitorReport(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new(42,"SECRETAPP",new(42,DateTimeOffset.UnixEpoch,@"C:\Users\SECRETUSER\secret.exe"),20,1,1,null,null,"secret", "NotSigned")],
            [new(42,"TCP","10.2.3.4",1,"192.0.2.1",2,"Established")],
            [new("Startup","Complete","SECRET_DETAIL",[new("SECRET_TASK","secret","TOKEN=SECRET_TOKEN")])],
            [new("resource-use","Info","High","SECRET_EXPLANATION",["SECRET_EVIDENCE"],"SECRET_RECOMMENDATION")]);
        var json = AiAnalysis.BuildPayload(report);
        Assert.DoesNotContain("SECRET",json);
        Assert.DoesNotContain("192.0.2.1",json);
        Assert.Contains("resource-use",json);
    }
    [Fact] public void LaunchHistoryAndBackupEvidenceStayOutOfCloudPayload()
    {
        var report = new LocalMonitorReport(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [],
            [new("AppUsage","Partial","SECRET_USAGE",[new(@"C:\Users\SECRETUSER\game.exe","HKCU UserAssist","{\"LastRecordedLaunchUtc\":\"SECRET_DATE\"}")]),
             new("Backups","Partial","SECRET_BACKUP",[new("TargetName","File History",@"\\SECRET_NAS\backup")])],
            []);
        var json = AiAnalysis.BuildPayload(report);
        Assert.DoesNotContain("SECRET",json);
        Assert.DoesNotContain("UserAssist",json);
    }
    [Theory]
    [InlineData("http://example.com/responses")]
    [InlineData("https://user:secret@example.com/responses")]
    [InlineData("https://example.com/responses?key=secret")]
    [InlineData("https://example.com/responses#fragment")]
    public void UnsafeProviderUrlsAreRejected(string url) => Assert.Throws<ArgumentException>(() => AiAnalysis.ValidateEndpoint(url));
}
