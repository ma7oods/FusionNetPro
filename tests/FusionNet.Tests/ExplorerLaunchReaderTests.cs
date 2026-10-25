using System.Buffers.Binary;
using FusionNet.Windows.Monitoring;

namespace FusionNet.Tests;

// Offline boundary cases for the UserAssist enumeration loop; no live registry access.
public class ExplorerLaunchReaderTests
{
    private const int Ok=0,MoreData=234,NoMoreItems=259;
    private static readonly DateTimeOffset Now=new(2026,10,24,12,0,0,TimeSpan.Zero);
    private static (int,string,uint,uint,byte[]) Valid(DateTimeOffset date)
    {var bytes=new byte[72];BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(60),date.ToFileTime());return (Ok,@"P:\ncc.rkr",3,72,bytes);}
    private static readonly (int,string,uint,uint,byte[]) End=(NoMoreItems,"",0,0,[]);

    [Fact] public void PreCancelledTokenInvokesNoEnumeration()
    {
        var calls=0;using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ExplorerLaunchReader.Collect(_=>{calls++;return End;},()=>TimeSpan.Zero,Now,cancel.Token));
        Assert.Equal(0,calls);
    }
    [Fact] public void SkippedValuesDoNotHideAFollowingValidRecord()
    {
        var results=new[]{(MoreData,"",0u,0u,Array.Empty<byte>()),(Ok,@"P:\ncc.rkr",1u,72u,new byte[72]),(Ok,@"P:\ncc.rkr",3u,16u,new byte[72]),Valid(Now.AddDays(-1)),End};
        var section=ExplorerLaunchReader.Collect(i=>results[i],()=>TimeSpan.Zero,Now,default);
        Assert.Equal("Partial",section.Status);Assert.Single(section.Items);
    }
    [Fact] public void OtherNativeErrorIsUnavailable()
    {
        var section=ExplorerLaunchReader.Collect(i=>i==0?Valid(Now):(5,"",0,0,[]),()=>TimeSpan.Zero,Now,default);
        Assert.Equal("Unavailable",section.Status);Assert.Empty(section.Items);
    }
    [Fact] public void EnumerationStopsAtTwoThousandCallsAndCapsRowsAtTwoHundred()
    {
        var calls=0;
        var section=ExplorerLaunchReader.Collect(_=>{calls++;return Valid(Now.AddDays(-1));},()=>TimeSpan.Zero,Now,default);
        Assert.Equal(2000,calls);Assert.Equal(200,section.Items.Count);
    }
    [Fact] public void ElapsedBudgetStopsEnumeration()
    {
        var calls=0;
        ExplorerLaunchReader.Collect(_=>{calls++;return Valid(Now);},()=>calls>=3?TimeSpan.FromSeconds(2.1):TimeSpan.Zero,Now,default);
        Assert.Equal(3,calls);
    }
    [Fact] public void OldRecordIsOnlyAReviewCandidate()
    {
        var section=ExplorerLaunchReader.Collect(i=>i==0?Valid(Now.AddMonths(-7)):End,()=>TimeSpan.Zero,Now,default);
        Assert.Equal("Partial",section.Status);
        Assert.Contains("\"ReviewOlderThanSixMonths\":true",section.Items[0].Detail);
        Assert.Contains("not complete usage history",section.Items[0].Detail);
    }
}
