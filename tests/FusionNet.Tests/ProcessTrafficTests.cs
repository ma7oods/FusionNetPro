using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class ProcessTrafficTests
{
    private static readonly ProcessIdentity Identity = new(42,DateTimeOffset.UnixEpoch,@"C:\app.exe");
    [Fact] public void BytesAreAttributedAndDividedByActualInterval()
    {
        var counter = new ProcessTrafficCounter([Identity]);
        counter.Add(42,1000,false); counter.Add(42,4000,true); counter.Add(999,50000,true);
        var row = Assert.Single(counter.Finish(TimeSpan.FromSeconds(2),0,[Identity]));
        Assert.Equal(500,row.UploadBytesPerSecond); Assert.Equal(2000,row.DownloadBytesPerSecond);
    }
    [Fact] public void LostEventsOrReusedPidNeverBecomeZeroRates()
    {
        var counter = new ProcessTrafficCounter([Identity]);
        Assert.Null(Assert.Single(counter.Finish(TimeSpan.FromSeconds(1),1,[Identity])).DownloadBytesPerSecond);
        Assert.Null(Assert.Single(counter.Finish(TimeSpan.FromSeconds(1),0,[Identity with { StartedUtc = Identity.StartedUtc.AddSeconds(1) }])).DownloadBytesPerSecond);
        Assert.Null(Assert.Single(counter.Finish(TimeSpan.Zero,0,[Identity])).DownloadBytesPerSecond);
    }
    [Fact] public void MeasuredIdleIsZeroButExitedIsUnknown()
    {
        var counter = new ProcessTrafficCounter([Identity]);
        Assert.Equal(0,Assert.Single(counter.Finish(TimeSpan.FromSeconds(1),0,[Identity])).DownloadBytesPerSecond);
        Assert.Null(Assert.Single(counter.Finish(TimeSpan.FromSeconds(1),0,[])).DownloadBytesPerSecond);
    }
}
