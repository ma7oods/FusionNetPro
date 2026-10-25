param([ValidateSet('Connections','Defender','Startup','Services','Tasks','Extensions','Backups','InstalledApps','Hardware','SecurityProducts')] [string]$Section)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
function Row($name, $source, $detail, $processId = $null) {
    @{ Name = ([string]$name).Substring(0, [Math]::Min(256, ([string]$name).Length)); Source = [string]$source;
       Detail = ([string]$detail).Substring(0, [Math]::Min(2048, ([string]$detail).Length)); Pid = $processId }
}
$rows = @()
$connections = @()
$detail = ''
function NoLinks($path) {
    $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        $item = $item.Parent
    }
    return $true
}
switch ($Section) {
    'SecurityProducts' {
        $rows = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop | Select-Object -First 201 | ForEach-Object {
            Row $_.displayName 'Windows Security Center' ([string]$_.pathToSignedProductExe + '; ' + [string]$_.pathToSignedReportingExe)
        })
    }
    'Connections' {
        $connections = @(@(Get-NetTCPConnection -ErrorAction Stop | Select-Object -First 2001 | ForEach-Object {
            @{ Pid = [int]$_.OwningProcess; Protocol = 'TCP'; LocalAddress = $_.LocalAddress; LocalPort = [int]$_.LocalPort;
               RemoteAddress = $_.RemoteAddress; RemotePort = [int]$_.RemotePort; State = [string]$_.State }
        }) + @(Get-NetUDPEndpoint -ErrorAction Stop | Select-Object -First 2001 | ForEach-Object {
            @{ Pid = [int]$_.OwningProcess; Protocol = 'UDP'; LocalAddress = $_.LocalAddress; LocalPort = [int]$_.LocalPort;
               RemoteAddress = $null; RemotePort = $null; State = 'Bound' }
        }))
        $detail = 'TCP IPv4/IPv6 endpoints; UDP bound endpoints only. Connections are not transfer rates.'
    }
    'Defender' {
        $status = Get-MpComputerStatus -ErrorAction Stop
        $rows += Row 'Protection status' 'Get-MpComputerStatus' ($status | Select-Object AMRunningMode,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated,QuickScanEndTime,FullScanEndTime | ConvertTo-Json -Compress)
        $rows += @(Get-MpThreatDetection -ErrorAction Stop | Select-Object -First 201 | ForEach-Object {
            Row ([string]$_.ThreatID) 'Get-MpThreatDetection (history; not necessarily active)' ($_ | Select-Object ThreatID,ActionSuccess,ThreatStatusID,CurrentThreatExecutionStatusID,InitialDetectionTime,RemediationTime | ConvertTo-Json -Compress)
        })
        $rows += @(Get-MpThreat -ErrorAction Stop | Select-Object -First 201 | ForEach-Object {
            Row $_.ThreatName 'Get-MpThreat' ($_ | Select-Object ThreatName,IsActive,DidThreatExecute,SeverityID | ConvertTo-Json -Compress)
        })
    }
    'Startup' {
        foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
            foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
                $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
                try {
                    foreach ($sub in @('Software\Microsoft\Windows\CurrentVersion\Run', 'Software\Microsoft\Windows\CurrentVersion\RunOnce')) {
                        $key = $base.OpenSubKey($sub, $false)
                        if ($null -eq $key) { continue }
                        try { foreach ($name in @($key.GetValueNames() | Select-Object -First 201)) { $rows += Row $name "$hive/$view/$sub" ($key.GetValue($name, '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) } }
                        finally { $key.Dispose() }
                    }
                } finally { $base.Dispose() }
            }
        }
        foreach ($folder in @([Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('CommonStartup'))) {
            if ($folder -and (Test-Path -LiteralPath $folder)) {
                $rows += @(Get-ChildItem -LiteralPath $folder -File -ErrorAction Stop | Select-Object -First 201 | ForEach-Object { Row $_.Name 'Startup folder (shortcut not executed)' $_.FullName })
            }
        }
    }
    'Services' {
        $rows = @(Get-CimInstance Win32_Service -ErrorAction Stop | Select-Object -First 501 | ForEach-Object {
            Row $_.Name 'Win32_Service' ($_ | Select-Object State,StartMode,StartName,PathName | ConvertTo-Json -Compress) ([int]$_.ProcessId)
        })
    }
    'Tasks' {
        $rows = @(Get-ScheduledTask -ErrorAction Stop | Select-Object -First 501 | ForEach-Object {
            Row ($_.TaskPath + $_.TaskName) 'Get-ScheduledTask' (@{ State = [string]$_.State; Principal = $_.Principal.UserId;
                Actions = @($_.Actions | Select-Object -First 5 | Select-Object Execute,Arguments) } | ConvertTo-Json -Compress -Depth 5)
        })
    }
    'Extensions' {
        foreach ($browser in @('Google\Chrome\User Data','Microsoft\Edge\User Data','BraveSoftware\Brave-Browser\User Data')) {
            $root = Join-Path $env:LOCALAPPDATA $browser
            if (!(Test-Path -LiteralPath $root)) { continue }
            if (!(NoLinks $root)) { continue }
            foreach ($profile in @(Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -eq 'Default' -or $_.Name -like 'Profile *' } | Select-Object -First 10)) {
                $ext = Join-Path $profile.FullName 'Extensions'
                if (!(Test-Path -LiteralPath $ext)) { continue }
                if (!(NoLinks $ext)) { continue }
                foreach ($id in @(Get-ChildItem -LiteralPath $ext -Directory | Where-Object { !($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } | Select-Object -First 100)) {
                    $rows += Row $id.Name "$browser/$($profile.Name)" 'Installed extension directory; permissions and activity not verified. Review browser extension manager.'
                }
            }
        }
        $detail = 'Limited Chromium directory inventory: at most 10 profiles and 100 extension IDs per profile; linked paths skipped. Firefox, permissions and behavior are not inspected.'
    }
    'Backups' {
        try {
            $service = Get-Service fhsvc -ErrorAction Stop
            $rows += Row 'File History service' 'Get-Service fhsvc' ([string]$service.Status + '; service state does not prove a backup exists.')
        } catch { $rows += Row 'File History service' 'Get-Service fhsvc' 'Service state unavailable; no backup conclusion.' }
        $config = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\FileHistory\Configuration'
        if (Test-Path -LiteralPath $config) { $rows += Row 'File History configuration' 'Local configuration' 'Configuration directory exists; destination and last successful backup not verified.' }
        try {
            if (!('FnpFileHistory' -as [type])) {
                Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FnpFileHistory {
 [ComImport,Guid("6A5FEA5B-BF8F-4EE5-B8C3-44D8A0D7331C"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Config {
  [PreserveSig]int LoadConfiguration();
  [PreserveSig]int CreateDefaultConfiguration([MarshalAs(UnmanagedType.Bool)]bool overwrite);
  [PreserveSig]int SaveConfiguration();
  [PreserveSig]int AddRemoveExcludeRule([MarshalAs(UnmanagedType.Bool)]bool add,int category,[MarshalAs(UnmanagedType.BStr)]string item);
  [PreserveSig]int GetIncludeExcludeRules([MarshalAs(UnmanagedType.Bool)]bool include,int category,out IntPtr iterator);
  [PreserveSig]int GetLocalPolicy(int kind,out ulong value);
  [PreserveSig]int SetLocalPolicy(int kind,ulong value);
  [PreserveSig]int GetBackupStatus(out int value);
  [PreserveSig]int SetBackupStatus(int value);
  [PreserveSig]int GetDefaultTarget(out Target target);
  [PreserveSig]int ValidateTarget([MarshalAs(UnmanagedType.BStr)]string url,out int value);
  [PreserveSig]int ProvisionAndSetNewTarget([MarshalAs(UnmanagedType.BStr)]string url,[MarshalAs(UnmanagedType.BStr)]string name);
  [PreserveSig]int ChangeDefaultTargetRecommendation([MarshalAs(UnmanagedType.Bool)]bool recommend);
  [PreserveSig]int QueryProtectionStatus(out uint state,[MarshalAs(UnmanagedType.BStr)]out string until);
 }
 [ComImport,Guid("D87965FD-2BAD-4657-BD3B-9567EB300CED"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Target {
  [PreserveSig]int GetStringProperty(int kind,[MarshalAs(UnmanagedType.BStr)]out string value);
  [PreserveSig]int GetNumericalProperty(int kind,out ulong value);
 }
 static string Bound(string value,int max) { return value==null?null:value.Substring(0,Math.Min(value.Length,max)); }
 public static string[] Read() {
  object native=null;Target target=null;
  try {
   native=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ED43BB3C-09E9-498A-9DF6-2177244C6DB4")));
   var config=(Config)native;
   Marshal.ThrowExceptionForHR(config.LoadConfiguration());
   int status;Marshal.ThrowExceptionForHR(config.GetBackupStatus(out status));
   string name=null,url=null,until=null;uint protection=0;
   if(config.GetDefaultTarget(out target)>=0&&target!=null) {
    if(target.GetStringProperty(0,out name)<0)name=null;
    if(target.GetStringProperty(1,out url)<0)url=null;
   }
   var protectionResult=config.QueryProtectionStatus(out protection,out until);
   return new[]{status>=0&&status<4?new[]{"Disabled","DisabledByPolicy","Enabled","Rehydrating"}[status]:"Unknown",
    Bound(name,256),Bound(url,512),protectionResult>=0?protection.ToString("X"):null,protectionResult>=0?Bound(until,128):null};
  } finally {
   if(target!=null&&Marshal.IsComObject(target))Marshal.ReleaseComObject(target);
   if(native!=null&&Marshal.IsComObject(native))Marshal.ReleaseComObject(native);
  }
 }
}
'@
            }
            $fh = [FnpFileHistory]::Read()
            $rows += Row 'File History configuration evidence' 'IFhConfigMgr / IFhTarget (read-only)' (@{
                ConfigurationStatus=$fh[0]; TargetName=$fh[1]; ConfiguredTarget=$fh[2]; ProtectionStateHex=$fh[3]; ProtectedUntilLocalText=$fh[4];
                LastSuccessfulBackupUtc=$null; Limitation='Configured target is not a verified reachable backup. Protected-until text is locale-specific protection coverage, not last successful backup time. API is deprecated.'
            } | ConvertTo-Json -Compress)
        } catch { $rows += Row 'File History configuration evidence' 'IFhConfigMgr / IFhTarget (read-only)' 'Configuration API unavailable or unreadable. No target or successful backup time is inferred.' }
        $detail = 'Configured status/target and protection-through text are shown only when the official API returns them. Last successful backup time is unverified. Open File History and verify a restore; other backup providers are not covered.'
    }
    'InstalledApps' {
        foreach ($root in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
            if (Test-Path -LiteralPath $root) {
                $rows += @(Get-ChildItem -LiteralPath $root | Select-Object -First 501 | Get-ItemProperty | Where-Object DisplayName | ForEach-Object {
                    Row $_.DisplayName 'Uninstall registry (no usage history)' (@{ Publisher=$_.Publisher; InstallDate=$_.InstallDate; EstimatedSizeKB=$_.EstimatedSize; LastUsed='Unavailable' } | ConvertTo-Json -Compress)
                })
            }
        }
        $detail = 'Installation dates are not last-used dates. Six-month inactivity cannot be inferred.'
    }
    'Hardware' {
        $rows = @(Get-CimInstance -Namespace root/LibreHardwareMonitor -ClassName Sensor -ErrorAction Stop | Select-Object -First 201 | ForEach-Object {
            Row $_.Name 'LibreHardwareMonitor WMI (external official tool must be running)' (@{ Type=$_.SensorType; Value=$_.Value; Identifier=$_.Identifier } | ConvertTo-Json -Compress)
        })
        $detail = 'Read existing LHM WMI only. No driver, hardware controls or LHM process is started.'
    }
}
$cap = if ($Section -in @('Services','Tasks','InstalledApps')) { 500 } else { 200 }
$partial = $rows.Count -gt $cap -or $connections.Count -gt 2000 -or $Section -in @('Extensions','Backups','InstalledApps')
@{ Name=$Section; Status=$(if ($partial) { 'Partial' } else { 'Complete' }); Detail=$detail;
   Items=@($rows | Select-Object -First $cap); Connections=@($connections | Select-Object -First 2000) } | ConvertTo-Json -Depth 8 -Compress
