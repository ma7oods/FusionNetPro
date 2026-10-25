#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using System.Reflection;
using System.Text.Json;
using FusionNet.Windows.Monitoring;

var assembly = typeof(WindowsLocalMonitor).Assembly;
var parse = typeof(WindowsLocalMonitor).GetMethod("ParseQuery", BindingFlags.Static | BindingFlags.NonPublic)!;
var good = """{"Status":"Complete","Detail":"test","Items":[],"Connections":[]}""";
Require(parse.Invoke(null, [good]) is not null, "Valid empty response rejected.");
foreach (var bad in new[] { "{}", "null", "[]", good.Replace("Complete", "Safe"), good.Replace("\"Items\":[]", "\"Items\":null"), good.Replace("\"Items\":[]", "\"Items\":[null]"), good.Replace("\"Connections\":[]", "\"Connections\":[null]") })
{
    try { parse.Invoke(null, [bad]); throw new Exception("Malformed response was accepted."); }
    catch (TargetInvocationException ex) when (ex.InnerException is JsonException) { }
}
var runner = assembly.GetType("FusionNet.Windows.Network.NetworkCommandRunner")!;
var drain = runner.GetMethod("ReadAllBytesAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
using var small = new MemoryStream(new byte[1024]);
Require((await (Task<byte[]>)drain.Invoke(null, [small])!).Length == 1024, "Small output changed.");
using var oversized = new MemoryStream(new byte[4 * 1024 * 1024 + 1]);
Require((await (Task<byte[]>)drain.Invoke(null, [oversized])!).Length == 0, "Oversized output not rejected.");
var scriptMethod = typeof(WindowsLocalMonitor).GetMethod("ReadScript", BindingFlags.Static | BindingFlags.NonPublic)!;
var script = (string)scriptMethod.Invoke(null, ["InventoryQueries.ps1"])!;
Require(script.Contains("Get-MpComputerStatus") && script.Contains("Get-MpThreatDetection"), "Official Defender queries missing.");
foreach (var mutation in new[] { "Start-MpScan", "Remove-MpThreat", "Stop-Process", "Set-MpPreference", "Remove-Item", "Invoke-Expression" })
    Require(!script.Contains(mutation, StringComparison.OrdinalIgnoreCase), "Mutator in read-only inventory: " + mutation);
var recovery = (string)scriptMethod.Invoke(null, ["GameBoostRecovery.ps1"])!;
Require(recovery.IndexOf("Move-Item", StringComparison.Ordinal) < recovery.LastIndexOf("Unregister-ScheduledTask", StringComparison.Ordinal), "Recovery removed before durable status.");
Require(recovery.Contains("Description -ne $ctx.Group") && recovery.Contains("Get-ScheduledTask -TaskPath '\\'"), "Independent task ownership/root path missing.");
var stop = assembly.GetType("FusionNet.Windows.Monitoring.EtwSessionStop")!;
var layout = stop.GetNestedType("Properties", BindingFlags.NonPublic)!;
Require(global::System.Runtime.InteropServices.Marshal.SizeOf(layout) == 120 &&
    global::System.Runtime.InteropServices.Marshal.OffsetOf(layout,"EventsLost").ToInt32() == 88, "x64 ETW native layout mismatch.");
try { stop.GetMethod("StopAndReadLost", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,["OtherSession"]); throw new Exception("Non-owned trace accepted."); }
catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException) { }
Console.WriteLine("Local monitor checks passed: valid/invalid JSON, bounded tool output, embedded read-only queries. No scan performed.");
return 0;

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
