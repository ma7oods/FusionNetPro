namespace FusionNet.Core.Models;

public sealed record DnsInspectionRequest(string Host, string Server, string RecordType);
public sealed record DnsRecordObservation(string Name, string Type, int TtlSeconds, string Value);
public sealed record DnsInspectionReport(DateTimeOffset TimestampUtc, string Host, string Server, string RecordType,
    double ElapsedMilliseconds, string ResponseCode, IReadOnlyList<DnsRecordObservation> Records, bool Partial);
public sealed record HardwareSensorObservation(string Name, string Type, double Value, string Unit, string Identifier);
public sealed record HardwareSensorReport(DateTimeOffset TimestampUtc, string Status, string Detail, IReadOnlyList<HardwareSensorObservation> Sensors);
