# Read-only: disk health from the Windows Storage API and 7-day counts from the System/Application event logs.
$ErrorActionPreference = 'SilentlyContinue'
$systemDisk = (Get-Partition -DriveLetter $env:SystemDrive[0] | Get-Disk).Number
$disks = @(Get-PhysicalDisk | ForEach-Object {
    [pscustomobject]@{ Name = [string]$_.FriendlyName; Media = [string]$_.MediaType; Health = [string]$_.HealthStatus; System = ($_.DeviceId -eq [string]$systemDisk) }
})
$since = (Get-Date).AddDays(-7)
$count = { param($filter) $filter.StartTime = $since; @(Get-WinEvent -FilterHashtable $filter -MaxEvents 500).Count }
[pscustomobject]@{
    Disks = $disks
    Crashes = (& $count @{ LogName = 'Application'; ProviderName = 'Application Error'; Id = 1000 })
    PowerLoss = (& $count @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power'; Id = 41 })
    Hardware = (& $count @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-WHEA-Logger' })
    DiskErrors = (& $count @{ LogName = 'System'; ProviderName = 'disk'; Id = 7, 51, 153 })
} | ConvertTo-Json -Depth 3 -Compress
