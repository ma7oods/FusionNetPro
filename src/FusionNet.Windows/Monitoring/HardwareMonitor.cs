using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Windows.Monitoring;

public sealed class HardwareMonitor : IHardwareMonitor
{
    public async Task<HardwareSensorReport> ReadAsync(CancellationToken token)
    {
        try
        {
            var query=await WindowsLocalMonitor.QueryAsync("Hardware",token).ConfigureAwait(false);
            var sensors=ToolboxValidation.Sensors(query.Items);
            return new(DateTimeOffset.UtcNow,sensors.Count<query.Items.Length?"Partial":query.Status,
                "المصدر: LibreHardwareMonitor WMI الخارجي. القيم عينة مؤرخة؛ لا driver أو قراءة مباشرة للعتاد. " +
                (sensors.Count==0?"لم تتوفر حساسات صالحة؛ شغّل الأداة الرسمية وتحقق من توفر WMI.":$"حساسات مقروءة: {sensors.Count}. قيم ناقصة/غير مدعومة: {query.Items.Length-sensors.Count}."),sensors);
        }
        catch(OperationCanceledException) {throw;}
        catch(Exception ex) when(ex is InvalidOperationException or global::System.Text.Json.JsonException or IOException)
        {return new(DateTimeOffset.UtcNow,"Unavailable","لم يتوفر مصدر LibreHardwareMonitor WMI. لم تُثبّت أو تُشغّل أداة أو driver بواسطة FusionNet.",[]);}
    }
}
