using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;
public class ProcessPresentationTests
{
    private static readonly ProcessObservation Process=new(42,"chrome",new(42,DateTimeOffset.UnixEpoch,@"C:\Apps\chrome.exe"),7,100,2,1024,0,"sample");
    [Theory][InlineData("CHROME")][InlineData("42")][InlineData("apps")]
    public void SearchMatchesNamePidAndPath(string query)=>Assert.True(ProcessPresentation.Matches(Process,query,false,false));
    [Fact] public void UnknownMeasurementsDoNotPassMeasuredFilters()
    {
        Assert.False(ProcessPresentation.Matches(Process with{UploadBytesPerSecond=null,DownloadBytesPerSecond=null},"",true,false));
        Assert.False(ProcessPresentation.Matches(Process with{CpuPercent=null,MemoryPercent=null},"",false,true));
        Assert.True(ProcessPresentation.Matches(Process,"",true,true));
    }
    [Fact] public void UnknownPurposeIsNotInvented()
    {
        Assert.Contains("متصفح",ProcessPresentation.Purpose("chrome"));
        Assert.Contains("غير معروفة",ProcessPresentation.Purpose("random-unknown.exe"));
    }
    [Fact] public void EnglishPurposePreservesUncertaintyAndArabicCompatibility()
    {
        Assert.Equal(ProcessPresentation.Purpose("chrome"),ProcessPresentation.Purpose("chrome",false));
        Assert.Contains("not identity or safety",ProcessPresentation.Purpose("chrome",true));
        Assert.Contains("unknown",ProcessPresentation.Purpose("random-unknown.exe",true));
    }
}
