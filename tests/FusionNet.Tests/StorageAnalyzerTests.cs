using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class StorageAnalyzerTests
{
    [Fact] public async Task ReportsLargestFilesDuplicatesAndInstallerCandidatesWithoutDeletingAnything()
    {
        var root = Path.Combine(Path.GetFullPath("qa"),"storage-fixture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var downloads=Path.Combine(root,"Downloads"); Directory.CreateDirectory(downloads);
        try
        {
            for(var index=0;index<25;index++) await File.WriteAllBytesAsync(Path.Combine(downloads,index+".bin"),new byte[index+1]);
            await File.WriteAllBytesAsync(Path.Combine(root,"a.png"),[1,2,3]);
            await File.WriteAllBytesAsync(Path.Combine(root,"b.png"),[1,2,3]);
            await File.WriteAllTextAsync(Path.Combine(root,"setup.exe"),"synthetic installer name only");
            File.SetLastWriteTimeUtc(Path.Combine(downloads,"0.bin"),DateTime.UtcNow.AddDays(-200));
            var report=await new LocalStorageAnalyzer().ScanAsync(root,true,default);
            Assert.Equal(20,report.LargestFiles.Count);
            Assert.True(report.LargestFiles[0].ObservedBytes>=report.LargestFiles[1].ObservedBytes);
            Assert.Equal(2,Assert.Single(report.IdenticalPhotoCandidates).Count);
            Assert.Single(report.InstallerCandidates);
            Assert.Single(report.OldDownloads);
            Assert.Equal(28,Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Count());
        }
        finally
        {
            foreach(var file in Directory.EnumerateFiles(downloads)) File.Delete(file);
            Directory.Delete(downloads);
            foreach(var file in Directory.EnumerateFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
    [Fact] public async Task CancellationDoesNotStartEnumeration()
    {
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new LocalStorageAnalyzer().ScanAsync("not-a-real-folder",false,cancellation.Token));
    }
}
