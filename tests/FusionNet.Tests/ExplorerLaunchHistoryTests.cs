using System.Buffers.Binary;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class ExplorerLaunchHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026,10,24,12,0,0,TimeSpan.Zero);
    [Fact] public void ModernRecordPreservesDecodedPathAndUtcEvidence()
    {
        var date=Now.AddDays(-2);
        var result=ExplorerLaunchHistory.Parse(@"P:\Nccf\سياق\ovaf\t・nzr.rkr",Record(date),Now);
        Assert.NotNull(result);Assert.Equal(@"C:\Apps\سياق\bins\g・ame.exe",result.Entry);
        Assert.Equal(date,result.LastRecordedLaunchUtc);Assert.False(result.OlderThanSixMonths);
    }
    [Theory][InlineData(0)][InlineData(16)][InlineData(67)][InlineData(68)][InlineData(71)][InlineData(73)]
    public void UnknownRecordLayoutsAreNotGuessed(int length)=>Assert.Null(ExplorerLaunchHistory.Parse(@"P:\ncc.rkr",new byte[length],Now));
    [Theory][InlineData(-1)][InlineData(0)][InlineData(long.MaxValue)]
    public void InvalidFileTimesStayUnknown(long value)
    {var bytes=new byte[72];BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(60),value);Assert.Null(ExplorerLaunchHistory.Parse(@"P:\ncc.rkr",bytes,Now));}
    [Fact] public void FutureAndPreModernDatesAreRejected()
    {
        Assert.Null(ExplorerLaunchHistory.Parse(@"P:\ncc.rkr",Record(Now.AddSeconds(1)),Now));
        Assert.Null(ExplorerLaunchHistory.Parse(@"P:\ncc.rkr",Record(new(1970,1,1,0,0,0,TimeSpan.Zero)),Now));
    }
    [Theory][InlineData("HRZR_PGYFRFFVBA")][InlineData(@"P:\ncc.yax")][InlineData("ncc.rkr")][InlineData(@"\\freire\funer\ncc.rkr")][InlineData("P:\\ncc.rkr\n")]
    public void NonExecutableAndUnsafeNamesAreNotProgramEvidence(string name)=>Assert.Null(ExplorerLaunchHistory.Parse(name,Record(Now),Now));
    [Fact] public void KnownFolderTokensArePreservedWithoutInventingTheirLocation()
    {
        var result=ExplorerLaunchHistory.Parse(@"{6Q809377-6NS0-444O-8957-N3773S02200R}\Ncc\ncc.rkr",Record(Now.AddMonths(-7)),Now);
        Assert.NotNull(result);Assert.StartsWith("{6D809377-6AF0-444B-8957-A3773F02200E}",result.Entry);
        Assert.True(result.OlderThanSixMonths);
    }
    [Fact] public void SixCalendarMonthBoundaryIsExplicit()
    {
        Assert.False(ExplorerLaunchHistory.Parse(@"P:\ncc.rkr",Record(Now.AddMonths(-6)),Now)!.OlderThanSixMonths);
        Assert.True(ExplorerLaunchHistory.Parse(@"P:\ncc.rkr",Record(Now.AddMonths(-6).AddTicks(-1)),Now)!.OlderThanSixMonths);
    }
    private static byte[] Record(DateTimeOffset date)
    {var result=new byte[72];BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(60),date.ToFileTime());return result;}
}
