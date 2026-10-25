namespace FusionNet.Setup;

/// <summary>Generates the per-user uninstaller script that ships inside the install folder.</summary>
internal static class UninstallScript
{
    public static string Build(string installDirectory)
    {
        var directory = Escape(installDirectory);
        var desktop = Escape(SetupPaths.DesktopShortcut);
        var startShortcut = Escape(SetupPaths.StartMenuShortcut);
        var startFolder = Escape(SetupPaths.StartMenuFolder);
        var registryKey = SetupPaths.UninstallKeyPath.Replace("'", "''");
        var ownedFiles = string.Join(", ", SetupPaths.InstalledFileNames.Select(name => "'" + Escape(name) + "'"));

        return $$"""
param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$dir = [IO.Path]::GetFullPath('{{directory}}').TrimEnd('\')
if ($dir -eq [IO.Path]::GetPathRoot($dir).TrimEnd('\')) { throw 'Refusing to uninstall from a drive root.' }
if ((Test-Path -LiteralPath $dir) -and ((Get-Item -LiteralPath $dir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Refusing to uninstall through a directory link.'
}
$desktopLnk = '{{desktop}}'
$startLnk = '{{startShortcut}}'
$startFolder = '{{startFolder}}'
$uninstallKey = 'HKCU:\{{registryKey}}'
$appExe = Join-Path $dir 'FusionNetPro.exe'
$registration = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
$registeredHere = $registration -and [string]::Equals(([string]$registration.InstallLocation).TrimEnd('\'), $dir, [StringComparison]::OrdinalIgnoreCase)
$recognized = [IO.File]::Exists((Join-Path $dir 'payload.sha256')) -and ([IO.File]::Exists($appExe) -or [IO.File]::Exists((Join-Path $dir 'uninstall.ps1')))
if ((Test-Path -LiteralPath $dir) -and -not $registeredHere -and -not $recognized) {
    throw 'This folder is not a recognized FusionNet Pro installation.'
}

if (-not $Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $answer = [System.Windows.Forms.MessageBox]::Show(
        ('هل أنت متأكد من إزالة FusionNet Pro؟' + [Environment]::NewLine + [Environment]::NewLine + 'سيتم حذف ملفات البرنامج والاختصارات فقط. ستبقى إعداداتك وملفاتك الأخرى محفوظة.'),
        'إزالة FusionNet Pro',
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question,
        [System.Windows.Forms.MessageBoxDefaultButton]::Button2,
        [System.Windows.Forms.MessageBoxOptions]::RtlReading -bor [System.Windows.Forms.MessageBoxOptions]::RightAlign)
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }
}

Get-Process -Name 'FusionNetPro' -ErrorAction SilentlyContinue | ForEach-Object {
    $process = $_
    try {
        if (-not [string]::Equals($process.Path, $appExe, [StringComparison]::OrdinalIgnoreCase)) { return }
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) { Stop-Process -Id $process.Id -Force -ErrorAction Stop }
        if (-not $process.WaitForExit(3000)) { throw 'The installed app is still running.' }
    }
    finally { $process.Dispose() }
}

# An elevated app hides its path from this script and cannot be stopped here; its locked exe shows it still runs.
# Stop before deleting anything, so the uninstaller and registration stay usable for a retry.
if ([IO.File]::Exists($appExe)) {
    try { [IO.File]::Open($appExe, 'Open', 'ReadWrite', 'None').Dispose() }
    catch [IO.IOException] {
        if (-not $Quiet) {
            [System.Windows.Forms.MessageBox]::Show(
                'FusionNet Pro بعده شغال (غالبًا كمسؤول). سكّره من أيقونة الشريط وبعدين أعد الإزالة.',
                'إزالة FusionNet Pro',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        }
        exit 1
    }
}

# Remove only files shipped by setup; a user may have saved unrelated files beside the app.
foreach ($name in @({{ownedFiles}})) {
    $file = Join-Path $dir $name
    if ([IO.File]::Exists($file)) { Remove-Item -LiteralPath $file -Force -ErrorAction Stop }
}
if ((Test-Path -LiteralPath $dir) -and -not (Get-ChildItem -LiteralPath $dir -Force | Select-Object -First 1)) {
    # shortcut: a shell can lock an empty folder; remove it manually after that shell closes.
    try { [IO.Directory]::Delete($dir, $false) } catch [IO.IOException] { }
}

# A stale uninstaller must not unregister a newer installation in another folder.
if ($registeredHere) {
    foreach ($shortcut in @($desktopLnk, $startLnk)) {
        if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    }
    if ((Test-Path -LiteralPath $startFolder) -and -not (Get-ChildItem -LiteralPath $startFolder -Force | Select-Object -First 1)) {
        [IO.Directory]::Delete($startFolder, $false)
    }
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force
}

if (-not $Quiet) {
    [System.Windows.Forms.MessageBox]::Show(
        'تمت إزالة FusionNet Pro بنجاح. بقيت إعداداتك وأي ملفات أخرى محفوظة.',
        'FusionNet Pro',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
exit 0
""";
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
