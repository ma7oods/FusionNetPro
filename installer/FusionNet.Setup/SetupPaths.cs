using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace FusionNet.Setup;

/// <summary>Well known locations used by the installer and the generated uninstaller.</summary>
internal static class SetupPaths
{
    public const string ProductName = "FusionNet Pro";
    public const string AppExeName = "FusionNetPro.exe";
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FusionNetPro";
    public static readonly string[] InstalledFileNames = [AppExeName, "README.txt", "THIRD-PARTY-NOTICES.txt", "payload.sha256", "uninstall.ps1"];

    public static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    public static string DefaultInstallDirectory =>
        Path.Combine(LocalAppData, "Programs", ProductName);

    public static string UserDataDirectory => Path.Combine(LocalAppData, "FusionNetPro");

    public static string LogFile => Path.Combine(UserDataDirectory, "install.log");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify), ProductName + ".lnk");

    public static string StartMenuFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs, Environment.SpecialFolderOption.DoNotVerify), ProductName);

    public static string StartMenuShortcut => Path.Combine(StartMenuFolder, ProductName + ".lnk");

    public static string AppExeIn(string installDirectory) => Path.Combine(installDirectory, AppExeName);

    public static string ValidateInstallDirectory(string requested)
    {
        var full = Path.GetFullPath(requested.Trim().Trim('"'));
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("اختر مجلداً مخصصاً للبرنامج، وليس جذر القرص.");

        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var special in new[] { UserDataDirectory, LocalAppData,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
        {
            if (string.Equals(full, special.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("اختر مجلداً فرعياً مخصصاً لبرنامج FusionNet Pro.");
        }

        if (Directory.Exists(full))
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("اختر مجلداً عادياً، وليس رابطاً إلى مجلد آخر.");
            var recognized = File.Exists(AppExeIn(full)) && File.Exists(Path.Combine(full, "payload.sha256"));
            if (Directory.EnumerateFileSystemEntries(full).Any() && !recognized)
                throw new InvalidOperationException("هذا المجلد يحتوي على ملفات أخرى. اختر مجلداً فارغاً أو مجلد تثبيت FusionNet Pro الحالي.");
            foreach (var name in InstalledFileNames)
            {
                var target = Path.Combine(full, name);
                if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("مجلد التثبيت يحتوي على رابط ملف غير آمن.");
            }
        }
        return full;
    }

    /// <summary>Reads the install location from the per-user uninstall registry key, if present.</summary>
    public static string? FindInstalledDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            if (key?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                return location;
        }
        catch
        {
            // ignore: fall back to the default directory
        }
        return null;
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        foreach (var candidate in LogCandidates())
        {
            try
            {
                var folder = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(candidate, line);
                return;
            }
            catch
            {
                // try the next location; logging must never break installation
            }
        }
    }

    private static IEnumerable<string> LogCandidates()
    {
        yield return LogFile;
        yield return Path.Combine(Path.GetTempPath(), "FusionNetPro-Setup.log");
        string baseLog = string.Empty;
        try { baseLog = Path.Combine(AppContext.BaseDirectory, "FusionNetPro-Setup.log"); } catch { }
        if (baseLog.Length > 0) yield return baseLog;
    }
}
