using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;
using FusionNet.Core.Abstractions;
using FusionNet.Windows.Native;
using FusionNet.Windows.System;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace FusionNet.Windows.Monitoring;

public sealed record TrafficSampleRequest(Guid Id, string UserSid, ProcessIdentity Owner);
public sealed class WindowsTrafficSampler : IProcessTrafficSampler
{
    public async Task<ProcessTrafficReport> SampleAsync(CancellationToken token)
    {
        using var owner = Process.GetCurrentProcess();
        var request = new TrafficSampleRequest(Guid.NewGuid(), WindowsIdentity.GetCurrent().User!.Value,
            new(owner.Id, owner.StartTime.ToUniversalTime(), Environment.ProcessPath!));
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        using var worker = await Task.Run(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, Arguments = "--traffic-worker=" + data }), token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("تعذر تشغيل عامل القياس.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await worker.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        var path = Path.Combine(ProtectedSessionStorage.DirectoryPath(request.Id), "traffic.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidOperationException("لم تتوفر عينة ETW موثوقة. لا تُعرض قيم بديلة.");
        new ProtectedSessionStorage(request.Id, request.UserSid).ValidateExisting();
        var report = JsonSerializer.Deserialize<ProcessTrafficReport>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false))
            ?? throw new JsonException("Invalid traffic report.");
        if (report.Processes is null || report.Processes.Count > 5000 || !double.IsFinite(report.WindowSeconds) || report.WindowSeconds is <= 0 or > 30 ||
            report.Processes.Any(row => row.UploadBytesPerSecond is { } up && (!double.IsFinite(up) || up < 0) ||
                row.DownloadBytesPerSecond is { } down && (!double.IsFinite(down) || down < 0))) throw new JsonException("Invalid traffic bounds.");
        return report;
    }

    public static async Task<int> RunWorkerAsync(TrafficSampleRequest request)
    {
        if (!ElevationService.CheckIsElevated() || request.Id == Guid.Empty || WindowsIdentity.GetCurrent().User?.Value != request.UserSid ||
            !GameBoostWorker.IdentityAlive(request.Owner)) return 1;
        var storage = new ProtectedSessionStorage(request.Id, request.UserSid);
        try
        {
            storage.Create();
            var before = ReadIdentities();
            var counter = new ProcessTrafficCounter(before);
            var name = "FusionNetPro.Net." + request.Id.ToString("N");
            using var session = new TraceEventSession(name, TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate) { StopOnDispose = false };
            var lost = -1;
            var started = false;
            var stopped = false;
            try
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
                started = true;
                var kernel = session.Source.Kernel;
                var watch = Stopwatch.StartNew();
                var start = (DateTime.UtcNow - session.Source.SessionStartTime.ToUniversalTime()).TotalMilliseconds;
                double endMilliseconds = double.MaxValue;
                void Add(int pid, int size, bool receive, double timestamp)
                { if (timestamp >= start && timestamp <= Volatile.Read(ref endMilliseconds)) counter.Add(pid, size, receive); }
                kernel.TcpIpSend += data => Add(data.ProcessID, data.size, false, data.TimeStampRelativeMSec);
                kernel.TcpIpRecv += data => Add(data.ProcessID, data.size, true, data.TimeStampRelativeMSec);
                kernel.TcpIpSendIPV6 += data => Add(data.ProcessID, data.size, false, data.TimeStampRelativeMSec);
                kernel.TcpIpRecvIPV6 += data => Add(data.ProcessID, data.size, true, data.TimeStampRelativeMSec);
                kernel.UdpIpSend += data => Add(data.ProcessID, data.size, false, data.TimeStampRelativeMSec);
                kernel.UdpIpRecv += data => Add(data.ProcessID, data.size, true, data.TimeStampRelativeMSec);
                kernel.UdpIpSendIPV6 += data => Add(data.ProcessID, data.size, false, data.TimeStampRelativeMSec);
                kernel.UdpIpRecvIPV6 += data => Add(data.ProcessID, data.size, true, data.TimeStampRelativeMSec);
                var processing = Task.Run(() => session.Source.Process());
                await Task.Delay(5000).ConfigureAwait(false);
                Volatile.Write(ref endMilliseconds, start + watch.Elapsed.TotalMilliseconds);
                watch.Stop();
                lost = EtwSessionStop.StopAndReadLost(name);
                stopped = true;
                await processing.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                if (counter.AttributedEvents == 0) lost = -1;
                var rates = counter.Finish(watch.Elapsed, lost, ReadIdentities());
                storage.Write("traffic.json", JsonSerializer.Serialize(new ProcessTrafficReport(DateTimeOffset.UtcNow, watch.Elapsed.TotalSeconds, lost,
                    lost == 0 ? "ETW TCP/UDP IPv4/IPv6; average bytes/sec over this sample, not adapter totals or packet capture." : "Incomplete or no attributable events; rates unavailable.", rates)));
                return 0;
            }
            finally
            {
                if (started && !stopped) try { EtwSessionStop.StopAndReadLost(name); } catch { /* no report if stop/statistics could not be verified */ }
            }
        }
        catch { return 1; }
    }

    private static IReadOnlyList<ProcessIdentity> ReadIdentities()
    {
        var list = new List<ProcessIdentity>();
        var all = Process.GetProcesses();
        try
        {
            foreach (var process in all.Take(5000))
                try { if (Win32Native.TryQueryProcessPath(process.Id, out var path, out _)) list.Add(new(process.Id, process.StartTime.ToUniversalTime(), path)); } catch { /* inaccessible processes remain unknown */ }
        }
        finally { foreach (var process in all) process.Dispose(); }
        return list;
    }
}
