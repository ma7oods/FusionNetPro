using FusionNet.Core.Models;

namespace FusionNet.Core.Monitoring;

public sealed record ProcessTrafficRate(ProcessIdentity Identity, double? UploadBytesPerSecond, double? DownloadBytesPerSecond);
public sealed record ProcessTrafficReport(DateTimeOffset TimestampUtc, double WindowSeconds, int EventsLost,
    string Status, IReadOnlyList<ProcessTrafficRate> Processes);
public sealed class ProcessTrafficCounter(IEnumerable<ProcessIdentity> identities)
{
    private readonly Dictionary<int, (ProcessIdentity Identity, long Upload, long Download)> _totals = identities.ToDictionary(identity => identity.Pid, identity => (identity, 0L, 0L));
    private bool _invalid;
    public int AttributedEvents { get; private set; }
    public void Add(int pid, int bytes, bool received)
    {
        if (bytes < 0) { _invalid = true; return; }
        if (!_totals.TryGetValue(pid, out var row)) return;
        AttributedEvents++;
        try
        {
            _totals[pid] = (row.Identity, received ? row.Upload : checked(row.Upload + bytes), received ? checked(row.Download + bytes) : row.Download);
        }
        catch (OverflowException) { _invalid = true; }
    }
    public IReadOnlyList<ProcessTrafficRate> Finish(TimeSpan interval, int eventsLost, IEnumerable<ProcessIdentity> finalIdentities)
    {
        var final = finalIdentities.ToDictionary(identity => identity.Pid);
        return _totals.Values.Select(row =>
        {
            var valid = !_invalid && eventsLost == 0 && interval > TimeSpan.Zero && final.TryGetValue(row.Identity.Pid, out var current) &&
                ProcessSampling.SameIdentity(current, row.Identity);
            return new ProcessTrafficRate(row.Identity, valid ? row.Upload / interval.TotalSeconds : null, valid ? row.Download / interval.TotalSeconds : null);
        }).ToArray();
    }
}
