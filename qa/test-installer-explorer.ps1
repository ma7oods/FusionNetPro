param(
    [switch]$SkipDefaultPathTest
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$setup = Join-Path $root "dist\FusionNetPro-Setup-3.0.1.exe"
if (-not (Test-Path $setup)) { throw "Setup exe not found: $setup" }

$failures = New-Object System.Collections.Generic.List[string]
function Check([string]$name, [bool]$condition, [string]$detail = "") {
    if ($condition) { Write-Host "PASS  $name" }
    else { Write-Host "FAIL  $name  $detail"; $failures.Add("$name $detail") }
}

# Scripts launched through Explorer run in the normal user context (like a real double-click),
# outside the DSH sandbox process tree.
$helper = Join-Path $PSScriptRoot "explorer-helper.ps1"
function New-ActionCmd([string]$action, [string]$resultFile) {
    $cmdPath = Join-Path $PSScriptRoot ("_run-" + $action + ".cmd")
    $content = "@echo off`r`n" +
        "powershell -NoProfile -ExecutionPolicy Bypass -File `"$helper`" -Action $action -ResultFile `"$resultFile`"`r`n"
    Set-Content -Path $cmdPath -Value $content -Encoding ASCII
    return $cmdPath
}

function Invoke-ExplorerAction([string]$action, [int]$timeoutSec = 180) {
    $resultFile = Join-Path $PSScriptRoot ("_result-" + $action + ".txt")
    Remove-Item $resultFile -Force -ErrorAction SilentlyContinue
    $cmdPath = New-ActionCmd $action $resultFile
    Start-Process -FilePath "explorer.exe" -ArgumentList "`"$cmdPath`"" | Out-Null
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $resultFile) {
            Start-Sleep -Milliseconds 300
            return (Get-Content $resultFile)
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Explorer action '$action' timed out; no result file."
}

function ResultMap([string[]]$lines) {
    $map = @{}
    foreach ($line in $lines) {
        $idx = $line.IndexOf('=')
        if ($idx -gt 0) { $map[$line.Substring(0, $idx)] = $line.Substring($idx + 1) }
    }
    return $map
}

$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FusionNetPro"
$startShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "FusionNet Pro\FusionNet Pro.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "FusionNet Pro.lnk"
$testDir = Join-Path $root "qa\_explorerinstall"

Write-Host "== 0. wizard preview (rendered by the real exe, normal user context) =="
$result = ResultMap (Invoke-ExplorerAction "preview" 120)
Check "preview exit 0" ($result["preview_exit"] -eq "0") ($result | Out-String)
Check "preview png created" ($result["preview_exists"] -eq "True")

Write-Host "== 1. silent install to custom dir =="
$result = ResultMap (Invoke-ExplorerAction "install" 300)
Check "install exit 0" ($result["install_exit"] -eq "0") ($result | Out-String)
Check "app exe installed" ($result["exe_exists"] -eq "True")
Check "readme installed" ($result["readme_exists"] -eq "True")
Check "uninstaller installed" ($result["uninstaller_exists"] -eq "True")
Check "install folder exists" ($result["dir_exists"] -eq "True")
Check "uninstall registry key exists" (Test-Path $regKey)
$props = Get-ItemProperty $regKey -ErrorAction SilentlyContinue
Check "registry DisplayName" ($props.DisplayName -eq "FusionNet Pro") "got=$($props.DisplayName)"
Check "registry InstallLocation" ($props.InstallLocation -and $props.InstallLocation.TrimEnd('\') -eq $testDir.TrimEnd('\')) "got=$($props.InstallLocation)"
Check "start menu shortcut exists" (Test-Path $startShortcut)
Check "desktop shortcut skipped (/nodesktop)" (-not (Test-Path $desktopShortcut))

Write-Host "== 2. installed app launches (normal user context) =="
$result = ResultMap (Invoke-ExplorerAction "runapp" 120)
Check "installed app alive" ($result["app_alive"] -eq "True") ($result | Out-String)
Check "installed app has main window" ([int64]$result["app_hwnd"] -ne 0) "hwnd=$($result['app_hwnd'])"
[void](Invoke-ExplorerAction "closeapp" 60)

Write-Host "== 3. uninstaller removes everything =="
$result = ResultMap (Invoke-ExplorerAction "uninstall" 180)
Check "uninstall exit 0" ($result["uninstall_exit"] -eq "0") ($result | Out-String)
Check "install folder deleted" ($result["dir_removed"] -eq "True")
Check "uninstall registry key deleted" (-not (Test-Path $regKey))
Check "start menu shortcut deleted" (-not (Test-Path $startShortcut))

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "RESULT: $($failures.Count) FAILURE(S)"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host "RESULT: ALL EXPLORER-CONTEXT INSTALLER TESTS PASSED"
exit 0
