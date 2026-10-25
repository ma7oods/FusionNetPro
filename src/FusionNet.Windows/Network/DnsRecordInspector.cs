using System.Diagnostics;
using System.Net;
using DnsClient;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Windows.Network;

public sealed class DnsRecordInspector(Func<LookupClientOptions, IDnsQuery>? factory = null) : IDnsInspector
{
    public async Task<DnsInspectionReport> QueryAsync(DnsInspectionRequest request, CancellationToken token)
    {
        request=ToolboxValidation.Normalize(request);
        token.ThrowIfCancellationRequested();
        var options=new LookupClientOptions(IPAddress.Parse(request.Server))
        {
            AutoResolveNameServers=false, UseCache=false, UseTcpFallback=true, UseRandomNameServer=false,
            ContinueOnDnsError=false, ContinueOnEmptyResponse=false, Retries=0, Timeout=TimeSpan.FromSeconds(3), ThrowDnsErrors=false
        };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(7));
        var client=factory?.Invoke(options) ?? new LookupClient(options);
        var watch=Stopwatch.StartNew();
        var response=await client.QueryAsync(request.Host,Enum.Parse<QueryType>(request.RecordType),QueryClass.IN,deadline.Token).ConfigureAwait(false);
        watch.Stop();
        var partial=response.Answers.Count>128 || response.Header.ResultTruncated;
        var records=response.Answers.Take(128).Select(record=>
        {
            var value=record.ToString();
            if(value.Length>2048) {value=value[..2048];partial=true;}
            return new DnsRecordObservation(record.DomainName.Value,record.RecordType.ToString(),record.InitialTimeToLive,value);
        }).ToArray();
        return new(DateTimeOffset.UtcNow,request.Host,request.Server,request.RecordType,watch.Elapsed.TotalMilliseconds,response.Header.ResponseCode.ToString(),records,partial);
    }
}
