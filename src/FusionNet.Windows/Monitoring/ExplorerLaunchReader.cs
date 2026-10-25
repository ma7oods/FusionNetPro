using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Windows.Monitoring;

internal static class ExplorerLaunchReader
{
    internal const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";
    internal static InventorySection Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(KeyPath,writable:false);
            if(key is null)return new("AppUsage","Unavailable","No readable current-account Explorer launch records. Absence does not mean an application was unused.",[]);
            var watch=Stopwatch.StartNew();
            return Collect(index=>
            {
                var name=new StringBuilder(2049);uint nameSize=2049;
                var bytes=new byte[72];uint size=72;
                var result=RegEnumValue(key.Handle,index,name,ref nameSize,IntPtr.Zero,out var type,bytes,ref size);
                return (result,name.ToString(),type,size,bytes);
            },()=>watch.Elapsed,DateTimeOffset.UtcNow,token);
        }
        catch(Exception ex)when(ex is UnauthorizedAccessException or IOException or SecurityException)
        {return new("AppUsage","Unavailable","Explorer launch history could not be read; no inactivity conclusion is available.",[]);}
    }
    // Test seam: the enumeration loop runs against fake RegEnumValue results without touching the live registry.
    internal static InventorySection Collect(Func<uint,(int Result,string Name,uint Type,uint Size,byte[] Data)> enumerate,Func<TimeSpan> elapsed,DateTimeOffset now,CancellationToken token)
    {
        var records=new List<AppLaunchEvidence>();
        for(uint index=0;index<2000&&elapsed()<=TimeSpan.FromSeconds(2);index++)
        {
            token.ThrowIfCancellationRequested();
            var (result,name,type,size,bytes)=enumerate(index);
            if(result==259)break;
            if(result==234)continue;
            if(result!=0)return new("AppUsage","Unavailable","Explorer launch enumeration failed; no inactivity conclusion is available.",[]);
            if(type!=3||size!=72)continue;
            if(ExplorerLaunchHistory.Parse(name,bytes,now) is {} record)records.Add(record);
        }
        var rows=records.OrderBy(record=>record.LastRecordedLaunchUtc).Take(200).Select(record=>new InventoryItem(record.Entry,
            "HKCU UserAssist executable records; Explorer launch evidence only",
            JsonSerializer.Serialize(new{record.LastRecordedLaunchUtc,ReviewOlderThanSixMonths=record.OlderThanSixMonths,
                Limitation="A recorded Explorer launch, not complete usage history or proof of six-month inactivity. Do not uninstall from this evidence alone."}))).ToArray();
        return new("AppUsage","Partial",$"Current account only; modern 72-byte records, up to 2000 values/2 seconds and 200 displayed entries. {records.Count} readable executable launch records. Known-folder tokens remain literal. Command-line/service launches and cleared/disabled tracking are not covered; an old record is only a review candidate.",rows);
    }
    // A fixed buffer rejects oversized/unrecognized values without allocating their registry payload.
    [DllImport("advapi32.dll",EntryPoint="RegEnumValueW",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int RegEnumValue(SafeRegistryHandle key,uint index,StringBuilder name,ref uint nameSize,IntPtr reserved,out uint type,[Out]byte[] data,ref uint size);
}
