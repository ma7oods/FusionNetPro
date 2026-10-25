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

function Run-Exe([string]$exe, [string]$argLine, [int]$timeoutSec = 240) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = $argLine
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $p.WaitForExit($timeoutSec * 1000)) {
        try { Stop-Process -Id $p.Id -Force } catch { }
        throw "Timeout after ${timeoutSec}s: $exe $argLine"
    }
    return $p.ExitCode
}

$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FusionNetPro"
$startShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "FusionNet Pro\FusionNet Pro.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "FusionNet Pro.lnk"
$defaultDir = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Programs\FusionNet Pro"
$startFolder = Split-Path -Parent $startShortcut

# A custom /D still uses the current user's registration and shortcuts.
foreach ($existing in @($regKey, $startFolder, $desktopShortcut, $defaultDir)) {
    if (Test-Path -LiteralPath $existing) {
        throw "Installer QA requires a clean test account. Existing installation or shortcut found: $existing"
    }
}
if (Get-Process FusionNetPro -ErrorAction SilentlyContinue) {
    throw "Installer QA cannot run while any FusionNetPro instance is open."
}

Write-Host "== 0. preview wizard render =="
$preview = Join-Path $root "qa\installer-preview.png"
Remove-Item $preview -Force -ErrorAction SilentlyContinue
$code = Run-Exe $setup "/preview=`"$preview`"" 120
Check "preview exit 0" ($code -eq 0) "code=$code"
Check "preview png created" (Test-Path $preview)

Write-Host "== 1. silent install to custom dir =="
$testDir = Join-Path $root ("qa\_installtest-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$code = Run-Exe $setup "/S /D=`"$testDir`" /nodesktop /nolaunch" 300
Check "silent install exit 0" ($code -eq 0) "code=$code"
Check "app exe installed" (Test-Path (Join-Path $testDir "FusionNetPro.exe"))
Check "readme installed" (Test-Path (Join-Path $testDir "README.txt"))
Check "uninstaller installed" (Test-Path (Join-Path $testDir "uninstall.ps1"))
Check "uninstall registry key exists" (Test-Path $regKey)
Check "start menu shortcut exists" (Test-Path $startShortcut)
Check "desktop shortcut skipped (/nodesktop)" (-not (Test-Path $desktopShortcut))
$installLocation = (Get-ItemProperty $regKey -ErrorAction SilentlyContinue).InstallLocation
Check "registry InstallLocation matches" ($installLocation -and $installLocation.TrimEnd('\') -eq $testDir.TrimEnd('\')) "got=$installLocation"
$displayName = (Get-ItemProperty $regKey -ErrorAction SilentlyContinue).DisplayName
Check "registry DisplayName" ($displayName -eq "FusionNet Pro") "got=$displayName"

Write-Host "== 1b. upgrade preserves user files =="
$originalAppHash = (Get-FileHash -LiteralPath (Join-Path $testDir 'FusionNetPro.exe') -Algorithm SHA256).Hash
$userNote = Join-Path $testDir 'my-note.txt'
Set-Content -LiteralPath $userNote -Value 'keep this user file' -Encoding UTF8
$code = Run-Exe $setup "/S /D=`"$testDir`" /nodesktop /nolaunch" 300
Check "upgrade exit 0" ($code -eq 0) "code=$code"
Check "upgrade payload is intact" ((Get-FileHash -LiteralPath (Join-Path $testDir 'FusionNetPro.exe') -Algorithm SHA256).Hash -eq $originalAppHash)
Check "upgrade preserves unrelated file" ((Get-Content -LiteralPath $userNote -Raw).Trim() -eq 'keep this user file')

Write-Host "== 2. installed app launches =="
$appExe = Join-Path $testDir "FusionNetPro.exe"
# UseShellExecute=false: the shell would raise a modal "publisher could not be verified"
# dialog for the unsigned binary and block the run.
$appInfo = New-Object System.Diagnostics.ProcessStartInfo
$appInfo.FileName = $appExe
$appInfo.WorkingDirectory = $testDir
$appInfo.UseShellExecute = $false
$app = [System.Diagnostics.Process]::Start($appInfo)
$deadline = (Get-Date).AddSeconds(40)
$ok = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited) { break }
    if ($app.MainWindowHandle -ne 0) { $ok = $true; break }
}
Check "installed app window appeared" $ok "exited=$($app.HasExited)"
if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force; Start-Sleep -Milliseconds 800 }

Write-Host "== 3. uninstaller removes everything =="
$uninstallScript = Join-Path $testDir "uninstall.ps1"
$code = Run-Exe "powershell.exe" "-NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`" -Quiet" 180
Check "uninstall exit 0" ($code -eq 0) "code=$code"
Check "program files removed" (-not (Test-Path -LiteralPath (Join-Path $testDir 'FusionNetPro.exe')))
Check "uninstall preserves unrelated file" ((Get-Content -LiteralPath $userNote -Raw).Trim() -eq 'keep this user file')
Remove-Item -LiteralPath $userNote -Force
[IO.Directory]::Delete($testDir, $false)
Check "test folder cleaned after preserving file" (-not (Test-Path -LiteralPath $testDir))
Check "uninstall registry key deleted" (-not (Test-Path $regKey))
Check "start menu shortcut deleted" (-not (Test-Path $startShortcut))

if (-not $SkipDefaultPathTest) {
    Write-Host "== 4. default path install (desktop shortcut + uninstall via setup /uninstall /quiet) =="
    $code = Run-Exe $setup "/S /nolaunch" 300
    Check "default install exit 0" ($code -eq 0) "code=$code"
    Check "default install path used" (Test-Path (Join-Path $defaultDir "FusionNetPro.exe")) "dir=$defaultDir"
    Check "desktop shortcut created" (Test-Path $desktopShortcut)
    $code = Run-Exe $setup "/uninstall /quiet" 180
    Check "setup /uninstall /quiet exit 0" ($code -eq 0) "code=$code"
    $removed = $false
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path $defaultDir)) { $removed = $true; break }
        Start-Sleep -Milliseconds 500
    }
    Check "default install folder deleted" $removed "still exists: $defaultDir"
    Check "desktop shortcut deleted" (-not (Test-Path $desktopShortcut))
    Check "registry cleaned" (-not (Test-Path $regKey))
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "RESULT: $($failures.Count) FAILURE(S)"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host "RESULT: ALL INSTALLER TESTS PASSED"
exit 0
