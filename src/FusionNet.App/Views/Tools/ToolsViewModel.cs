using System.Collections.ObjectModel;
using FusionNet.App.Mvvm;
using FusionNet.App.Localization;
using FusionNet.App.Services;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Tools;

public sealed record ToolSource(string Name,string Status,string Purpose,string License,string Url);
public sealed record SensorRow(HardwareSensorObservation Sensor)
{
    public string Name=>Sensor.Name;
    public string Value=>$"{Sensor.Value:0.##} {Sensor.Unit}";
    public string Detail=>$"{Sensor.Type} · {Sensor.Identifier}";
}
public sealed class ToolsViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private CancellationTokenSource? _operation;
    private string _host="example.com",_server="1.1.1.1",_recordType="A";
    private string _dnsStatus="Tools.Status.IdleDns";
    private string _hardwareStatus="Tools.Status.IdleHardware";
    private bool _busy;
    private bool _languageSubscribed;
    private string _sourceStatus="Tools.Status.IdleSource";
    public ToolsViewModel(IAppServices services)
    {
        _services=services;
        LookupCommand=new AsyncRelayCommand(LookupAsync,()=>!_busy);
        HardwareCommand=new AsyncRelayCommand(ReadHardwareAsync,()=>!_busy);
        CancelCommand=new RelayCommand(()=>_operation?.Cancel(),()=>_busy);
        OpenSourceCommand=new RelayCommand(value=>
        {
            if(value is ToolSource source && Sources.Contains(source))
                SourceStatus=ExternalLinks.TryOpen(source.Url)?"Tools.Text.SourceOpened":Loc.Format("Tools.Format.OpenFailed",source.Url);
        });
    }
    public IReadOnlyList<ToolSource> Sources => Catalog;
    public static IReadOnlyList<ToolSource> Catalog => [
        new("DnsClient.NET 1.8.0",Loc.T("Tools.Text.Bundled"),Loc.T("Tools.Text.DnsPurpose"),"Apache-2.0","https://github.com/MichaCo/DnsClient.NET"),
        new("Microsoft TraceEvent 3.2.8",Loc.T("Tools.Text.Bundled"),Loc.T("Tools.Text.EtwPurpose"),"MIT","https://github.com/microsoft/perfview"),
        new("LibreHardwareMonitor",Loc.T("Tools.Text.External"),Loc.T("Tools.Text.HardwarePurpose"),Loc.T("Tools.Text.HardwareLicense"),"https://github.com/LibreHardwareMonitor/LibreHardwareMonitor"),
        new("Sysinternals",Loc.T("Tools.Text.Optional"),Loc.T("Tools.Text.SysinternalsPurpose"),Loc.T("Tools.Text.SysinternalsLicense"),"https://learn.microsoft.com/sysinternals/")];
    public IReadOnlyList<string> RecordTypes {get;}=["A","AAAA","CNAME","MX","TXT","NS"];
    public string Host {get=>_host;set=>SetProperty(ref _host,value);}
    public string Server {get=>_server;set=>SetProperty(ref _server,value);}
    public string RecordType {get=>_recordType;set=>SetProperty(ref _recordType,value);}
    public string DnsStatus {get=>Loc.T(_dnsStatus);private set=>SetProperty(ref _dnsStatus,value);}
    public string HardwareStatus {get=>Loc.T(_hardwareStatus);private set=>SetProperty(ref _hardwareStatus,value);}
    public bool IsBusy=>_busy;
    public string SourceStatus{get=>Loc.T(_sourceStatus);private set=>SetProperty(ref _sourceStatus,value);}
    public ObservableCollection<DnsRecordObservation> Records {get;}=[];
    public ObservableCollection<SensorRow> Sensors {get;}=[];
    public AsyncRelayCommand LookupCommand {get;}
    public AsyncRelayCommand HardwareCommand {get;}
    public RelayCommand CancelCommand {get;}
    public RelayCommand OpenSourceCommand {get;}
    private void Busy(bool value)
    {
        _busy=value;OnPropertyChanged(nameof(IsBusy));LookupCommand.RaiseCanExecuteChanged();HardwareCommand.RaiseCanExecuteChanged();CancelCommand.RaiseCanExecuteChanged();
    }
    private async Task LookupAsync()
    {
        Busy(true);_operation=new();Records.Clear();
        try
        {
            DnsStatus=Loc.Format("Tools.Format.Querying",Server,Host);
            var report=await _services.DnsInspector.QueryAsync(new(Host,Server,RecordType),_operation.Token);
            foreach(var record in report.Records) Records.Add(record);
            DnsStatus=$"{report.Host} • {report.Server} • {report.ResponseCode} • {report.ElapsedMilliseconds:F1} ms • {report.TimestampUtc.ToLocalTime():HH:mm:ss}\n"+
                (report.Partial?Loc.T("Tools.Text.Partial"):report.Records.Count==0?Loc.T("Tools.Text.NoAnswers"):Loc.Format("Tools.Format.AnswerCount",report.Records.Count))+
                Loc.T("Tools.Text.DnsLimit");
        }
        catch(OperationCanceledException){DnsStatus="Tools.Text.DnsCancelled";}
        catch(ArgumentException ex){DnsStatus=ex.Message;}
        catch{DnsStatus="Tools.Text.DnsFailed";}
        finally{_operation.Dispose();_operation=null;Busy(false);}
    }
    private async Task ReadHardwareAsync()
    {
        Busy(true);_operation=new();Sensors.Clear();
        try
        {
            HardwareStatus="Tools.Text.HardwareBusy";
            var report=await _services.HardwareMonitor.ReadAsync(_operation.Token);
            foreach(var sensor in report.Sensors) Sensors.Add(new(sensor));
            HardwareStatus=$"{report.TimestampUtc.ToLocalTime():HH:mm:ss} • {report.Status}\n{report.Detail}";
        }
        catch(OperationCanceledException){HardwareStatus="Tools.Text.HardwareCancelled";}
        catch{HardwareStatus="Tools.Text.HardwareFailed";}
        finally{_operation.Dispose();_operation=null;Busy(false);}
    }
    public void Activate()
    {
        if(!_languageSubscribed){Loc.Instance.LanguageChanged+=OnLanguageChanged;_languageSubscribed=true;}
        OnLanguageChanged(null,EventArgs.Empty);
    }
    private void OnLanguageChanged(object? sender,EventArgs args)=>OnPropertyChanged(string.Empty);
    public void Deactivate()
    {
        _operation?.Cancel();
        if(_languageSubscribed){Loc.Instance.LanguageChanged-=OnLanguageChanged;_languageSubscribed=false;}
    }
}
