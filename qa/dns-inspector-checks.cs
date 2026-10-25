#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using System.Net;
using System.Reflection;
using DnsClient;
using DnsClient.Protocol;
using FusionNet.Core.Models;
using FusionNet.Windows.Network;

LookupClientOptions? captured=null;
var query=DispatchProxy.Create<IDnsQuery,QueryFixture>();
var fixture=(QueryFixture)(object)query;
var inspector=new DnsRecordInspector(options=>{captured=options;return query;});
await Reject(()=>inspector.QueryAsync(new("https://bad","1.1.1.1","A"),default));
Require(captured is null,"Invalid input reached client factory.");
using(var canceled=new CancellationTokenSource())
{canceled.Cancel();await Reject(()=>inspector.QueryAsync(new("example.com","1.1.1.1","A"),canceled.Token));}
Require(captured is null,"Canceled request reached client factory.");
var result=await inspector.QueryAsync(new("EXAMPLE.com.","1.1.1.1","a"),default);
Require(captured is {AutoResolveNameServers:false,UseCache:false,UseTcpFallback:true,Retries:0,ContinueOnEmptyResponse:false,ContinueOnDnsError:false},"Wrong query scope/cache/fallback options.");
Require(captured!.NameServers.Count()==1 && captured.NameServers.Single().Address=="1.1.1.1" && captured.NameServers.Single().Port==53,"Unrequested DNS resolver included.");
Require(result.Host=="example.com"&&result.Server=="1.1.1.1"&&result.Records.Count==1&&result.Records[0].Value.Contains("192.0.2.10")&&result.Records[0].TtlSeconds==60,"Record projection differs from fixture.");
Require(fixture.LastToken.CanBeCanceled&&fixture.Calls==1,"Deadline token missing.");
fixture.Count=200;
result=await inspector.QueryAsync(new("example.com","1.1.1.1","A"),default);
Require(result.Partial&&result.Records.Count==128,"Unbounded response projection.");
fixture.Count=1;fixture.Truncated=true;
result=await inspector.QueryAsync(new("example.com","1.1.1.1","A"),default);
Require(result.Partial,"Truncated reply appeared complete.");
fixture.Throw=true;
await Reject(()=>inspector.QueryAsync(new("example.com","1.1.1.1","A"),default));
Console.WriteLine("DNS inspector passed: validation, cancellation, exact resolver, cache/retry/fallback scope, result bounds and failed query. Fake client only; no network.");
return 0;

static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
static async Task Reject(Func<Task<DnsInspectionReport>> action){try{await action();}catch(Exception ex)when(ex is ArgumentException or OperationCanceledException or InvalidOperationException){return;}throw new Exception("Expected request rejection.");}
public class QueryFixture:DispatchProxy
{
    public int Calls,Count=1;public bool Throw,Truncated;public CancellationToken LastToken;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method?.Name!="QueryAsync")throw new Exception("Unexpected query operation.");
        Calls++;LastToken=(CancellationToken)args![^1]!;
        if(Throw)throw new InvalidOperationException("Synthetic lookup failure.");
        var response=DispatchProxy.Create<IDnsQueryResponse,ResponseFixture>();
        ((ResponseFixture)(object)response).Count=Count;
        ((ResponseFixture)(object)response).Truncated=Truncated;
        return Task.FromResult(response);
    }
}
public class ResponseFixture:DispatchProxy
{
    public int Count;public bool Truncated;
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>method?.Name switch
    {
        "get_Header"=>new DnsResponseHeader(1,(ushort)(Truncated?0x0200:0),1,Count,0,0),
        "get_Answers"=>Enumerable.Range(0,Count).Select(_=>(DnsResourceRecord)new ARecord(new ResourceRecordInfo("example.com",ResourceRecordType.A,QueryClass.IN,60,4),IPAddress.Parse("192.0.2.10"))).ToArray(),
        _=>throw new Exception("Unexpected response operation: "+method?.Name)
    };
}
