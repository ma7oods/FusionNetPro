using FusionNet.Core.Models;

namespace FusionNet.Core.Abstractions;

public interface IDnsInspector
{
    Task<DnsInspectionReport> QueryAsync(DnsInspectionRequest request, CancellationToken token);
}
public interface IHardwareMonitor
{
    Task<HardwareSensorReport> ReadAsync(CancellationToken token);
}

public interface ILocalMonitor
{
    Task<LocalMonitorReport> ScanAsync(bool includeSecurityInventory, CancellationToken cancellationToken);
}
public interface IProcessTrafficSampler
{
    Task<Monitoring.ProcessTrafficReport> SampleAsync(CancellationToken token);
}
