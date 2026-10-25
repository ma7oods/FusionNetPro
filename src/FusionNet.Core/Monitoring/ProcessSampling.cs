using FusionNet.Core.Models;

namespace FusionNet.Core.Monitoring;

public static class ProcessSampling
{
    public static bool SameIdentity(ProcessIdentity? before, ProcessIdentity? after) =>
        before is not null && after is not null && before.Pid == after.Pid && before.StartedUtc == after.StartedUtc &&
        before.Path.Equals(after.Path, StringComparison.OrdinalIgnoreCase);
    public static double? Cpu(ProcessIdentity? before, ProcessIdentity? after, TimeSpan? previousCpu,
        TimeSpan? currentCpu, TimeSpan interval, int processors)
    {
        if (!SameIdentity(before, after) ||
            previousCpu is null || currentCpu is null || currentCpu < previousCpu || interval <= TimeSpan.Zero || processors < 1)
            return null;
        return Math.Clamp((currentCpu.Value - previousCpu.Value).TotalSeconds / interval.TotalSeconds / processors * 100, 0, 100);
    }
}
