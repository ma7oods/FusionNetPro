using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class ToolboxValidationTests
{
    [Fact] public void ValidDnsInputIsNormalizedWithoutChangingTheRequestedServer()
    {
        var request=ToolboxValidation.Normalize(new(" مثال.com. "," 1.1.1.1 "," aaaa "));
        Assert.Equal("xn--mgbh0fb.com",request.Host);
        Assert.Equal("1.1.1.1",request.Server);
        Assert.Equal("AAAA",request.RecordType);
    }
    [Theory]
    [InlineData("https://example.com","1.1.1.1","A")]
    [InlineData("-bad.com","1.1.1.1","A")]
    [InlineData("example..com","1.1.1.1","A")]
    [InlineData("example.com..","1.1.1.1","A")]
    [InlineData("example.com","1","A")]
    [InlineData("example.com","0.0.0.0","A")]
    [InlineData("example.com","224.0.0.1","A")]
    [InlineData("example.com","dns.example.com","A")]
    [InlineData("example.com","1.1.1.1","AXFR")]
    public void InvalidRequestsAreRejectedBeforeNetwork(string host,string server,string type) =>
        Assert.Throws<ArgumentException>(()=>ToolboxValidation.Normalize(new(host,server,type)));
    [Fact] public void MalformedSensorsAreUnavailableInsteadOfZero()
    {
        var rows=ToolboxValidation.Sensors([
            new("CPU","WMI","{\"Type\":\"Temperature\",\"Value\":64.5,\"Identifier\":\"/cpu/temp/0\"}"),
            new("bad","WMI","{\"Type\":\"Load\",\"Value\":null}"),
            new("bad","WMI","not JSON"),
            new("bad","WMI","[]"),
            new("bad","WMI","{\"Type\":\"Load\",\"Value\":200,\"Identifier\":\"/bad\"}")]);
        var sensor=Assert.Single(rows); Assert.Equal(64.5,sensor.Value); Assert.Equal("°C",sensor.Unit);
    }
}
