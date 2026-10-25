#:property TargetFramework=net10.0-windows
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using FusionNet.Core.Models;
using FusionNet.Windows.Network;
using FusionNet.Windows.System;

// Read-only live checks: query resolvers and sample counters without applying network settings.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var activity = new ActivityLog();
var results = await new DnsService(activity).BenchmarkAsync(DnsCatalog.Recommended, cancellationToken: timeout.Token);
if (results.Count != DnsCatalog.Recommended.SelectMany(provider => provider.Servers).Distinct().Count())
    throw new InvalidOperationException("DNS benchmark omitted servers.");
foreach (var result in results)
{
    if (result.Success && (!double.IsFinite(result.LatencyMs) || result.LatencyMs < 0))
        throw new InvalidOperationException("Invalid DNS latency.");
    Console.WriteLine($"DNS {result.ProviderName} {result.Server}: " +
        (result.Success ? $"{result.LatencyMs:0.0} ms" : result.ErrorText));
}

using var monitor = new ThroughputMonitor();
var received = new TaskCompletionSource<ThroughputSample>(TaskCreationOptions.RunContinuationsAsynchronously);
monitor.SampleReceived += (_, args) => received.TrySetResult(args.Sample);
monitor.Start();
var sample = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token);
monitor.Stop();
if (monitor.IsRunning || !double.IsFinite(sample.DownloadBytesPerSecond) || sample.DownloadBytesPerSecond < 0 ||
    !double.IsFinite(sample.UploadBytesPerSecond) || sample.UploadBytesPerSecond < 0)
    throw new InvalidOperationException("Invalid throughput sample or monitor did not stop.");
Console.WriteLine($"Throughput: download {sample.DownloadBytesPerSecond:0} B/s; upload {sample.UploadBytesPerSecond:0} B/s");
Console.WriteLine($"DNS reachable: {results.Count(result => result.Success)}/{results.Count}");
return results.Any(result => result.Success) ? 0 : 2;
