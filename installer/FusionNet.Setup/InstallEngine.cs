using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace FusionNet.Setup;

internal readonly record struct InstallProgress(int Percent, string Message);

/// <summary>Install / uninstall engine. All work is synchronous; callers run it off the UI thread.</summary>
internal static class InstallEngine
{
    private const string PayloadResource = "payload.zip";
    private const string PayloadHashFile = "payload.sha256";

    public static string ResolveInstallDirectory(SetupOptions options)
    {
        var requested = options.InstallDirectory;
        if (string.IsNullOrWhiteSpace(requested)) requested = SetupPaths.FindInstalledDirectory();
        if (string.IsNullOrWhiteSpace(requested)) requested = SetupPaths.DefaultInstallDirectory;
        return Path.GetFullPath(requested.Trim().Trim('"'));
    }

    public static void RunInstall(SetupOptions options, IProgress<InstallProgress>? progress)
    {
        var directory = SetupPaths.ValidateInstallDirectory(ResolveInstallDirectory(options));
        SetupPaths.Log($"Install started. target='{directory}', desktopShortcut={options.CreateDesktopShortcut}, user='{Environment.UserName}'");
        SetupPaths.Log($"Environment: temp='{Path.GetTempPath()}', base='{AppContext.BaseDirectory}', cpu={Environment.ProcessorCount}, os='{Environment.OSVersion}', x64={Environment.Is64BitProcess}");

        Report(progress, 3, "التحضير للتثبيت…");

        // Stage inside the install folder instead of %TEMP%: a locked-down or sandboxed temp
        // folder must not be able to break the install, and the target folder is writable by definition.
        var temp = Path.Combine(directory, ".setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(temp);
            Report(progress, 12, "فكّ ضغط حزمة البرنامج…");
            ExtractPayload(temp);

            Report(progress, 34, "التحقق من سلامة ملفات البرنامج…");
            VerifyPayload(temp);

            Report(progress, 48, "إغلاق أي نسخة عاملة من البرنامج…");
            StopRunningApp(directory);

            Report(progress, 56, "نسخ ملفات البرنامج…");
            CopyDirectory(temp, directory);

            Report(progress, 72, "كتابة أداة الإزالة…");
            WriteUninstaller(directory);

            Report(progress, 82, "إنشاء الاختصارات…");
            CreateShortcuts(directory, options);

            Report(progress, 92, "تسجيل البرنامج في ويندوز…");
            WriteRegistry(directory);

            SetupPaths.Log("Install completed successfully.");
            Report(progress, 100, "تم التثبيت بنجاح.");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    public static int InstallSilent(SetupOptions options)
    {
        try
        {
            RunInstall(options, new Progress<InstallProgress>(_ => { }));
            SetupPaths.Log("Silent install completed.");
            if (options.LaunchAfterInstall) Launch(options);
            return 0;
        }
        catch (Exception ex)
        {
            SetupPaths.Log("Silent install failed: " + ex);
            return 1;
        }
    }

    public static int Uninstall(SetupOptions options)
    {
        try
        {
            var directory = ResolveInstallDirectory(options);
            SetupPaths.Log($"Uninstall requested. target='{directory}'");

            var script = "& {\r\n" + UninstallScript.Build(directory) + "\r\n}" + (options.Silent ? " -Quiet" : "");
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                WorkingDirectory = Environment.SystemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("تعذّر بدء أداة الإزالة.");
            process.WaitForExit();
            SetupPaths.Log($"Uninstall finished with exit code {process.ExitCode}.");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            SetupPaths.Log("Uninstall failed: " + ex);
            return 1;
        }
    }

    public static void Launch(SetupOptions options)
    {
        var directory = ResolveInstallDirectory(options);
        var exe = SetupPaths.AppExeIn(directory);
        if (!File.Exists(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = directory,
                UseShellExecute = true,
                Verb = "runas" // same as the shortcuts: Fusion Mode needs elevation; a declined prompt is logged below
            });
        }
        catch (Exception ex)
        {
            SetupPaths.Log("Could not launch the app: " + ex.Message);
        }
    }

    private static void Report(IProgress<InstallProgress>? progress, int percent, string message)
    {
        SetupPaths.Log($"{percent,3}% {message}");
        progress?.Report(new InstallProgress(percent, message));
    }

    private static void ExtractPayload(string destination)
    {
        using var stream = typeof(InstallEngine).Assembly.GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("حزمة التثبيت غير موجودة. الملف قد يكون تالفاً.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var extracted = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, normalized));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("حزمة التثبيت تحتوي على مسار غير صالح: " + entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            extracted++;
        }

        if (extracted == 0) throw new InvalidDataException("حزمة التثبيت فارغة.");
        SetupPaths.Log($"Payload extracted: {extracted} file(s).");
    }

    private static void VerifyPayload(string extractedDirectory)
    {
        var exe = SetupPaths.AppExeIn(extractedDirectory);
        if (!File.Exists(exe)) throw new InvalidDataException("ملف البرنامج FusionNetPro.exe غير موجود في الحزمة.");

        var hashFile = Path.Combine(extractedDirectory, PayloadHashFile);
        if (!File.Exists(hashFile)) return; // hash manifest is optional

        var expected = File.ReadAllText(hashFile).Trim();
        if (expected.Length < 32) return;
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("فشل التحقق من سلامة ملف البرنامج (قد يكون التنزيل تالفاً). أعد تنزيل الملف وحاول مرة أخرى.");

        SetupPaths.Log("Payload SHA-256 verified.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CreateShortcuts(string installDirectory, SetupOptions options)
    {
        var exe = SetupPaths.AppExeIn(installDirectory);
        try
        {
            ShortcutService.CreateShortcut(SetupPaths.StartMenuShortcut, exe, installDirectory, "FusionNet Pro — Network Optimizer");
        }
        catch (Exception ex)
        {
            SetupPaths.Log("Start menu shortcut failed: " + ex.Message);
        }

        if (!options.CreateDesktopShortcut) return;
        try
        {
            ShortcutService.CreateShortcut(SetupPaths.DesktopShortcut, exe, installDirectory, "FusionNet Pro — Network Optimizer");
        }
        catch (Exception ex)
        {
            // A redirected/read-only desktop must never fail the installation.
            SetupPaths.Log("Desktop shortcut failed: " + ex.Message);
        }
    }

    private static void WriteUninstaller(string installDirectory)
    {
        var scriptPath = Path.Combine(installDirectory, "uninstall.ps1");
        File.WriteAllText(scriptPath, UninstallScript.Build(installDirectory), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        SetupPaths.Log("Uninstaller script written: " + scriptPath);
    }

    private static void WriteRegistry(string installDirectory)
    {
        var exe = SetupPaths.AppExeIn(installDirectory);
        var script = Path.Combine(installDirectory, "uninstall.ps1");
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) powershell = "powershell.exe";

        using var key = Registry.CurrentUser.CreateSubKey(SetupPaths.UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("تعذّر إنشاء مفتاح إلغاء التثبيت في السجل.");

        key.SetValue("DisplayName", SetupPaths.ProductName);
        key.SetValue("DisplayVersion", "3.3.3");
        key.SetValue("Publisher", "FusionNet");
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("UninstallString", $"\"{powershell}\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"");
        key.SetValue("QuietUninstallString", $"\"{powershell}\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Quiet");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("EstimatedSize", EstimateKilobytes(installDirectory), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("Language", "ar");
        SetupPaths.Log("Uninstall registry entry written.");
    }

    private static int EstimateKilobytes(string directory)
    {
        long bytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(file).Length; } catch { }
            }
        }
        catch
        {
            // best effort
        }
        return (int)Math.Max(1, Math.Min(int.MaxValue, bytes / 1024));
    }

    private static void StopRunningApp(string installDirectory)
    {
        foreach (var process in Process.GetProcessesByName("FusionNetPro"))
        {
            try
            {
                if (process.HasExited || !string.Equals(process.MainModule?.FileName, SetupPaths.AppExeIn(installDirectory), StringComparison.OrdinalIgnoreCase)) continue;
                SetupPaths.Log($"Stopping running instance pid={process.Id}.");
                process.CloseMainWindow();
                if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5 && !SetupElevation.IsElevated)
            {
                // The shortcuts start the app elevated; only an elevated setup can close it, so take the elevated-retry path.
                throw new UnauthorizedAccessException("FusionNet Pro يشتغل كمسؤول. سكّره من أيقونة الشريط أو أعد التثبيت بصلاحية المسؤول.", ex);
            }
            catch (Exception ex)
            {
                SetupPaths.Log("Could not stop running instance: " + ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}

