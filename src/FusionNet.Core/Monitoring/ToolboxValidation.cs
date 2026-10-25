using FusionNet.Core.Models;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FusionNet.Core.Monitoring;

public static class ToolboxValidation
{
    public static DnsInspectionRequest Normalize(DnsInspectionRequest request)
    {
        var host=request.Host?.Trim() ?? "";
        if(host.EndsWith('.'))host=host[..^1];
        if(host.Length is 0 or > 512 || host.Any(char.IsControl)) throw new ArgumentException("أدخل اسم نطاق دون رابط أو مسار.");
        host=new IdnMapping().GetAscii(host).ToLowerInvariant();
        if(host.Length>253 || host.Split('.').Any(label=>label.Length is 0 or >63 || label[0]=='-' || label[^1]=='-' ||
            label.Any(character=>!char.IsAsciiLetterOrDigit(character)&&character!='-')))
            throw new ArgumentException("اسم النطاق غير صالح.");
        var server=request.Server?.Trim() ?? "";
        if(server.Length>128)throw new ArgumentException("عنوان خادم DNS أطول من الحد المدعوم.");
        if(!IPAddress.TryParse(server,out var address) || address.AddressFamily==AddressFamily.InterNetwork && server.Split('.').Length!=4)
            throw new ArgumentException("أدخل عنوان IP كاملًا لخادم DNS الذي تريد استعلامه.");
        if(address.IsIPv4MappedToIPv6) address=address.MapToIPv4();
        var bytes=address.GetAddressBytes();
        if(address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any)||address.Equals(IPAddress.Broadcast)||address.IsIPv6Multicast ||
            address.AddressFamily==AddressFamily.InterNetwork && (bytes[0]==0 || bytes[0]>=224))
            throw new ArgumentException("عنوان DNS غير صالح للاستعلام المباشر.");
        var type=request.RecordType?.Trim().ToUpperInvariant() ?? "";
        if(type is not ("A" or "AAAA" or "CNAME" or "MX" or "TXT" or "NS")) throw new ArgumentException("نوع السجل غير مدعوم.");
        return new(host,address.ToString(),type);
    }
    public static IReadOnlyList<HardwareSensorObservation> Sensors(IEnumerable<InventoryItem> items)
    {
        var sensors=new List<HardwareSensorObservation>();
        foreach(var item in items.Take(200))
        {
            try
            {
                using var json=JsonDocument.Parse(item.Detail);
                var root=json.RootElement;
                if(root.ValueKind!=JsonValueKind.Object)continue;
                if(!root.TryGetProperty("Type",out var typeNode)||typeNode.ValueKind!=JsonValueKind.String ||
                    !root.TryGetProperty("Value",out var valueNode)||valueNode.ValueKind!=JsonValueKind.Number ||
                    !valueNode.TryGetDouble(out var value)||!double.IsFinite(value) ||
                    !root.TryGetProperty("Identifier",out var idNode)||idNode.ValueKind!=JsonValueKind.String) continue;
                var type=typeNode.GetString()!; var id=idNode.GetString()!;
                var unit=type switch {"Temperature"=>"°C","Load" or "Control"=>"%","Fan"=>"RPM","Clock"=>"MHz","Power"=>"W","Voltage"=>"V","Data"=>"GB","SmallData"=>"MB","Throughput"=>"B/s",_=>null};
                if(unit is null || id.Length>512 || item.Name.Length>256 || type is "Load" or "Control" && value is <0 or >100 ||
                    type!="Temperature" && value<0 || type=="Temperature" && value< -273.15) continue;
                sensors.Add(new(item.Name,type,value,unit,id));
            }
            catch(JsonException) { /* malformed readings remain unavailable, never zero */ }
        }
        return sensors;
    }
}
