$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility
if (!('FnpFileHistory' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
public static class FnpFileHistory {
 public static bool Fail;
 public static string[] Read() {
  if(Fail)throw new InvalidOperationException("Synthetic unavailable API");
  return new[]{"Enabled","synthetic target","X:\\", "FF", "synthetic locale date"};
 }
}
'@
}
$script = [scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/FusionNet.Windows/Monitoring/InventoryQueries.ps1')))
$modulePath = $env:PSModulePath
function Get-Service { param($Name) [pscustomobject]@{Status='Running'} }
function Test-Path { param($LiteralPath) return $script:present }
function Get-ChildItem { param($LiteralPath) [pscustomobject]@{DisplayName='synthetic app';Publisher='synthetic';InstallDate='20200101';EstimatedSize=10} }
function Get-ItemProperty { param([Parameter(ValueFromPipeline=$true)]$InputObject) process { $InputObject } }
$count=0
try {
    foreach ($section in @('Backups','InstalledApps')) {
        foreach ($script:present in @($false,$true)) {
            $result = (& $script -Section $section) | ConvertFrom-Json
            if ($result.Status -ne 'Partial') { throw "$section claimed complete coverage from configuration/installation presence" }
            if ($section -eq 'InstalledApps' -and @($result.Items | Where-Object { ($_.Detail | ConvertFrom-Json).LastUsed -ne 'Unavailable' }).Count) { throw 'Invented usage history' }
            if ($section -eq 'Backups') {
                if ($result.Detail -notmatch 'Last successful backup time is unverified') { throw 'Missing backup coverage disclaimer' }
                $item = @($result.Items | Where-Object Source -eq 'IFhConfigMgr / IFhTarget (read-only)')[0].Detail | ConvertFrom-Json
                if ($item.ConfiguredTarget -ne 'X:\' -or $item.ConfigurationStatus -ne 'Enabled' -or $null -ne $item.LastSuccessfulBackupUtc) { throw 'Configured target lost or last-success time invented' }
            }
            $count++
        }
    }
    [FnpFileHistory]::Fail = $true
    $unavailable = (& $script -Section 'Backups') | ConvertFrom-Json
    if ($unavailable.Status -ne 'Partial' -or @($unavailable.Items | Where-Object { $_.Detail -like '*API unavailable*' }).Count -ne 1) { throw 'Unavailable File History API became verified' }
    $count++
    Write-Output "Inventory coverage: $count/5 passed; File History COM/service/filesystem/registry replaced, no real inventory."
} finally { $env:PSModulePath = $modulePath }
