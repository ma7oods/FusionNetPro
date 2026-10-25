#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using System.Text.Json;
using FusionNet.Windows.Monitoring;

// Live read-only inventory: run ONLY after the human explicitly authorizes this scope.
// No application startup, Firewall/task writes, ETW, cloud, DNS query, driver or installer.
if(args.Length==1&&args[0]=="--self-test")
{
    var fixture=Path.Combine(Directory.GetCurrentDirectory(),"qa","summary-fixture-"+Guid.NewGuid().ToString("N")+".json");
    const string synthetic="{\"Scope\":\"synthetic only\",\"InvariantErrors\":[]}";
    var original=Console.Out;
    try
    {
        using var captured=new StringWriter();Console.SetOut(captured);
        PersistSummary(fixture,synthetic);
        if(File.ReadAllText(fixture)!=synthetic||!captured.ToString().Contains(synthetic))
            throw new InvalidOperationException("Summary is missing from file or captured output.");
    }
    finally{Console.SetOut(original);if(File.Exists(fixture))File.Delete(fixture);}
    Console.WriteLine("Summary persistence: file and captured output match. Synthetic data only; no inventory.");
    return 0;
}
if(args.Length!=1||args[0]!="--approved-live-read-only")
{
    Console.WriteLine("NOT RUN: explicit human authorization is required before passing --approved-live-read-only. No inventory was collected.");
    return 2;
}
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(80));
var report=await new WindowsLocalMonitor().ScanAsync(true,deadline.Token);
var errors=new List<string>();
if(report.Processes.Count==0)errors.Add("No processes observed; process collection is unverified.");
if(report.Processes.Any(process=>process.CpuPercent is <0 or >100||process.MemoryPercent is <0 or >100||process.MemoryBytes is <0))
    errors.Add("Observed resource value outside its documented range.");
if(report.Processes.Any(process=>process.UploadBytesPerSecond is not null||process.DownloadBytesPerSecond is not null))
    errors.Add("Unexpected per-process byte rate without ETW.");
var summary=new
{
    Scope="One manually authorized read-only local inventory; no mutations or cloud/ETW requests.",
    report.StartedUtc,report.FinishedUtc,ProcessCount=report.Processes.Count,ConnectionCount=report.Connections.Count,
    MeasuredCpuCount=report.Processes.Count(process=>process.CpuPercent.HasValue),
    Sections=report.Sections.Select(section=>new{section.Name,section.Status,ItemCount=section.Items.Count}),
    FindingCount=report.Findings.Count,InvariantErrors=errors,
    Limit="Counts and availability only. This does not validate Firewall/task recovery, ETW, installation or game performance. No identities/paths/addresses/commands/detection details are saved here."
};
var path=Path.Combine(Directory.GetCurrentDirectory(),"qa","live-readonly-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json");
PersistSummary(path,JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Live read-only summary saved: "+path);
Console.WriteLine("Invariant errors: "+errors.Count+"; unavailable/partial sections remain explicitly unverified.");
return errors.Count==0?0:1;

static void PersistSummary(string path,string json)
{
    File.WriteAllText(path,json);
    Console.WriteLine(json);
}
