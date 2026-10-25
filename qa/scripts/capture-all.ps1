param(
    [string]$Exe = "dist\FusionNetPro-3.0.1.exe",
    [string]$OutDir = "qa\screenshots",
    [int]$WarmupSeconds = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not ("Win32Capture" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Capture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-Process FusionNetPro -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$exePath = (Resolve-Path $Exe).Path
$started = Get-Date
# Launch without ShellExecute: an unsigned binary started through the shell raises a modal
# "publisher could not be verified" dialog that blocks an unattended run forever.
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exePath
$startInfo.WorkingDirectory = Split-Path -Parent $exePath
$startInfo.UseShellExecute = $false
$proc = [System.Diagnostics.Process]::Start($startInfo)

function Wait-Window([int]$procId, [int]$timeoutSec = 25) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if (-not $p) { return $null }
        if ($p.MainWindowHandle -ne 0) { return $p }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

$p = Wait-Window $proc.Id
if (-not $p) {
    $alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    Write-Output "FAIL: no main window (alive=$([bool]$alive))"
    if ($alive) { Stop-Process -Id $proc.Id -Force }
    exit 2
}

Start-Sleep -Seconds $WarmupSeconds
$p.Refresh()
Write-Output "window title: '$($p.MainWindowTitle)' hwnd=$($p.MainWindowHandle)"

function Save-Shot([string]$name) {
    $p.Refresh()
    if ($p.HasExited) { throw "process exited before shot '$name' (code=$($p.ExitCode))" }
    [void][Win32Capture]::ShowWindow($p.MainWindowHandle, 9)   # SW_RESTORE
    [void][Win32Capture]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 900
    $rect = New-Object Win32Capture+RECT
    [void][Win32Capture]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { throw "invalid window bounds for '$name'" }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "shot $name -> $path ($w x $h)"
}

$pages = @(
    @{ key = "^1"; name = "01-dashboard" },
    @{ key = "^2"; name = "02-monitor" },
    @{ key = "^3"; name = "03-dns" },
    @{ key = "^4"; name = "04-tuner" },
    @{ key = "^5"; name = "05-gaming" },
    @{ key = "^6"; name = "06-appshield" },
    @{ key = "^7"; name = "07-activity" },
    @{ key = "^8"; name = "08-settings" }
)

Save-Shot "00-startup"
foreach ($pg in $pages) {
    $wsh = New-Object -ComObject WScript.Shell
    [void]$wsh.AppActivate($proc.Id)
    Start-Sleep -Milliseconds 350
    [System.Windows.Forms.SendKeys]::SendWait($pg.key)
    Start-Sleep -Milliseconds 1400
    Save-Shot $pg.name
}

$p.Refresh()
Write-Output "alive at end: $(-not $p.HasExited) responsive=$($p.Responding)"
if (-not $p.HasExited) { Stop-Process -Id $proc.Id -Force; Start-Sleep -Milliseconds 500 }

# Surface any .NET crash / WPF binding logger events raised during this run
$since = $started.AddSeconds(-5)
$events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match '\.NET Runtime|Application Error|Windows Error Reporting|WPF' }
if ($events) {
    Write-Output "--- crash/app events ---"
    $events | Select-Object TimeCreated, ProviderName, Id, LevelDisplayName |
        Format-Table -AutoSize | Out-String -Width 200 | Write-Output
} else {
    Write-Output "no crash events in Application log"
}
