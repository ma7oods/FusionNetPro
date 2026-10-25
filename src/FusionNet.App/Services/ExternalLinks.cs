using System.ComponentModel;
using System.Diagnostics;

namespace FusionNet.App.Services;

public static class ExternalLinks
{
    internal static ProcessStartInfo MaintenanceStartInfo(string action)=>action switch
    {
        "startup"=>new("ms-settings:startupapps"){UseShellExecute=true},
        "apps"=>new("ms-settings:appsfeatures"){UseShellExecute=true},
        "windowsupdate"=>new("ms-settings:windowsupdate"){UseShellExecute=true},
        "filehistory"=>new(global::System.IO.Path.Combine(Environment.SystemDirectory,"control.exe"),"/name Microsoft.FileHistory"){UseShellExecute=true},
        _=>throw new ArgumentException("Unknown Windows maintenance action.",nameof(action))
    };
    public static bool TryOpenMaintenance(string action)
    {
        var start=MaintenanceStartInfo(action);
        try{return Process.Start(start) is not null;}
        catch(Exception ex)when(ex is Win32Exception or InvalidOperationException){return false;}
    }
    public static bool TryOpen(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))return false;
        try{return Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true}) is not null;}
        catch(Exception ex)when(ex is Win32Exception or InvalidOperationException){return false;}
    }
}
