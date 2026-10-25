param(
    [Parameter(Mandatory = $true)][string]$Action,
    [Parameter(Mandatory = $true)][string]$ResultFile
)
# Executed by Explorer (normal user context) so the installer is tested exactly like a
# real double-click, outside the DSH sandbox process tree.
$root = $PSScriptRoot
$root = Split-Path -Parent $root
$setup = Join-Path $root 'dist\FusionNetPro-Setup-3.0.1.exe'
$testDir = Join-Path $root 'qa\_explorerinstall'
$lines = @()

# Start-Process -Wait can hang forever on a child that owns a hidden window, so every
# launch is bounded and killed on timeout instead.
function Wait-Exit([System.Diagnostics.Process]$p, [int]$timeoutSec) {
    if ($p.WaitForExit($timeoutSec * 1000)) { return $p.ExitCode }
    try { $p.Kill($true) } catch { }
    return -1
}

try {
    switch ($Action) {
        'preview' {
            $preview = Join-Path $root 'qa\installer-preview-explorer.png'
            Remove-Item $preview -Force -ErrorAction SilentlyContinue
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $setup
            $psi.Arguments = "/preview=`"$preview`""
            $psi.UseShellExecute = $false
            $p = [System.Diagnostics.Process]::Start($psi)
            $lines += "preview_exit=$(Wait-Exit $p 120)"
            $lines += "preview_exists=$(Test-Path $preview)"
        }
        'install' {
            # Custom target inside the workspace: always writable, even on a locked-down machine.
            Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $setup
            $psi.Arguments = "/S /D=`"$testDir`" /nodesktop /nolaunch"
            $psi.UseShellExecute = $false
            $p = [System.Diagnostics.Process]::Start($psi)
            $lines += "install_exit=$(Wait-Exit $p 300)"
            $lines += "dir_exists=$(Test-Path $testDir)"
            $lines += "exe_exists=$(Test-Path (Join-Path $testDir 'FusionNetPro.exe'))"
            $lines += "readme_exists=$(Test-Path (Join-Path $testDir 'README.txt'))"
            $lines += "uninstaller_exists=$(Test-Path (Join-Path $testDir 'uninstall.ps1'))"
        }
        'defaultinstall' {
            # The real user flow: default location, Start Menu shortcut and Add/Remove Programs entry.
            Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $setup
            $psi.Arguments = "/S /nolaunch"
            $psi.UseShellExecute = $false
            $p = [System.Diagnostics.Process]::Start($psi)
            $lines += "install_exit=$(Wait-Exit $p 300)"
            $defaultDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\FusionNet Pro'
            $lines += "default_dir=$defaultDir"
            $lines += "default_dir_exists=$(Test-Path $defaultDir)"
            $lines += "default_exe_exists=$(Test-Path (Join-Path $defaultDir 'FusionNetPro.exe'))"
            $lines += "default_uninstaller_exists=$(Test-Path (Join-Path $defaultDir 'uninstall.ps1'))"
            $regKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FusionNetPro'
            $lines += "registry_exists=$(Test-Path $regKey)"
            $props = Get-ItemProperty $regKey -ErrorAction SilentlyContinue
            $lines += "registry_displayname=$($props.DisplayName)"
            $lines += "registry_installlocation=$($props.InstallLocation)"
            $startLnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'FusionNet Pro\FusionNet Pro.lnk'
            $lines += "startmenu_shortcut=$(Test-Path $startLnk)"
            $desktopLnk = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'FusionNet Pro.lnk'
            $lines += "desktop_shortcut=$(Test-Path $desktopLnk)"
        }
        'runapp' {
            $exe = Join-Path $testDir 'FusionNetPro.exe'
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $exe
            $psi.WorkingDirectory = $testDir
            $psi.UseShellExecute = $false
            $app = [System.Diagnostics.Process]::Start($psi)
            $hwnd = 0
            $deadline = (Get-Date).AddSeconds(45)
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 500
                try { $app.Refresh() } catch { break }
                if ($app.HasExited) { break }
                if ($app.MainWindowHandle -ne 0) { $hwnd = $app.MainWindowHandle }
            }
            $lines += "app_alive=$(-not $app.HasExited)"
            $lines += "app_hwnd=$hwnd"
            $lines += "app_pid=$($app.Id)"
            $lines += "app_title=$($app.MainWindowTitle)"
        }
        'closeapp' {
            Get-Process FusionNetPro -ErrorAction SilentlyContinue | ForEach-Object {
                $lines += "closing pid=$($_.Id)"
                [void]$_.CloseMainWindow()
            }
            Start-Sleep -Seconds 3
            Get-Process FusionNetPro -ErrorAction SilentlyContinue | Stop-Process -Force
        }
        'uninstall' {
            $script = Join-Path $testDir 'uninstall.ps1'
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = 'powershell.exe'
            $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Quiet"
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $p = [System.Diagnostics.Process]::Start($psi)
            $lines += "uninstall_exit=$(Wait-Exit $p 180)"
            $removed = $false
            $deadline = (Get-Date).AddSeconds(25)
            while ((Get-Date) -lt $deadline) { if (-not (Test-Path $testDir)) { $removed = $true; break }; Start-Sleep -Milliseconds 500 }
            $lines += "dir_removed=$removed"
            $regKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FusionNetPro'
            $lines += "registry_exists=$(Test-Path $regKey)"
        }
        'uninstalldefault' {
            $defaultDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\FusionNet Pro'
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $setup
            $psi.Arguments = '/uninstall /quiet'
            $psi.UseShellExecute = $false
            $p = [System.Diagnostics.Process]::Start($psi)
            $lines += "uninstall_exit=$(Wait-Exit $p 180)"
            $removed = $false
            $deadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $deadline) { if (-not (Test-Path $defaultDir)) { $removed = $true; break }; Start-Sleep -Milliseconds 500 }
            $lines += "default_dir_removed=$removed"
            $regKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FusionNetPro'
            $lines += "registry_exists=$(Test-Path $regKey)"
        }
        default { $lines += "unknown_action=$Action" }
    }
}
catch {
    $lines += "error=$($_.Exception.Message)"
}
$lines += "done=$(Get-Date -Format o)"
Set-Content -Path $ResultFile -Value $lines -Encoding UTF8
