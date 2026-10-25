#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/FusionNet.App/FusionNet.App.csproj

using System.Reflection;
using FusionNet.App.Views.Dns;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

// Exercise the actual view model without activating it or touching network/settings services.
var failures = new List<string>();
Check("failed resolvers cannot be applied as fastest", () =>
{
    var model = Publish(new DnsBenchmarkResult("cloudflare", "Cloudflare", "1.1.1.1", 0, false));
    Require(model.BestResult is null && !model.ApplyFastestCommand.CanExecute(null), "Failed DNS was selected as best.");
    Require(model.Results.All(row => !row.IsBest), "Failed DNS was highlighted as best.");
});
Check("fastest measured server is primary and failed backups are excluded", () =>
{
    var model = Publish(
        new("cloudflare", "Cloudflare", "1.1.1.1", 30, true),
        new("cloudflare", "Cloudflare", "1.0.0.1", 5, true),
        new("cloudflare", "Cloudflare", "192.0.2.1", 0, false),
        new("google", "Google", "8.8.8.8", 10, true));
    Require(model.BestResult?.Server == "1.0.0.1" && model.ApplyFastestCommand.CanExecute(null), "Fastest resolver missing.");
    var method = typeof(DnsViewModel).GetMethod("GetFastestServers", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var servers = method.Invoke(model, null);
    Require(((IEnumerable<string>)servers!).SequenceEqual(["1.0.0.1", "1.1.1.1"]), "Apply list differs from successful measurements.");
});
Check("empty results disable apply", () =>
{
    var model = Publish();
    Require(!model.HasBestResult && !model.ApplyFastestCommand.CanExecute(null), "Apply enabled without a measurement.");
});
foreach (var failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine($"DNS view-model checks: {3 - failures.Count}/3 passed");
return failures.Count == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add("FAIL " + name + ": " + ex.Message); }
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static DnsViewModel Publish(params DnsBenchmarkResult[] results)
{
    var model = new DnsViewModel(DispatchProxy.Create<IAppServices, UnusedServices>());
    var field = typeof(DnsViewModel).GetField("_rawResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
    ((List<DnsBenchmarkResult>)field.GetValue(model)!).AddRange(results);
    typeof(DnsViewModel).GetMethod("PublishResults", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
    return model;
}

public class UnusedServices : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        throw new InvalidOperationException("Unexpected service call: " + method?.Name);
}
