#:property TargetFramework=net10.0-windows
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../installer/FusionNet.Setup/FusionNet.Setup.csproj

using System.Diagnostics;
using System.Reflection;
using System.Text;

// Exercises only disposable folders beneath qa; never installs, registers, or changes network settings.
var workspace = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "qa"));
var root = Path.Combine(workspace, "_installer-safety-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var assembly = Assembly.Load("FusionNetPro-Setup");
var paths = assembly.GetType("FusionNet.Setup.SetupPaths", true)!;
var validate = paths.GetMethod("ValidateInstallDirectory", BindingFlags.Public | BindingFlags.Static)!;
var build = assembly.GetType("FusionNet.Setup.UninstallScript", true)!.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;
var owned = (string[])paths.GetField("InstalledFileNames", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
var checks = 0;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine("PASS " + message);
}

bool Rejects(string path)
{
    try { validate.Invoke(null, [path]); return false; }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return true; }
}

bool Throws(Action action)
{
    try { action(); return false; }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { return true; }
}

void RemoveOwnedFiles(string directory, bool shouldSucceed = true, bool encoded = false, bool holdWorkingDirectory = false)
{
    var script = (string)build.Invoke(null, [directory])!;
    Check(!script.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase) && !script.Contains("rmdir", StringComparison.OrdinalIgnoreCase), "cleanup does not expand paths through cmd");
    var scriptPath = Path.Combine(directory, "uninstall.ps1");
    if (!encoded) File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
    var info = new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        WorkingDirectory = holdWorkingDirectory ? directory : root,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true
    };
    foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass" }) info.ArgumentList.Add(arg);
    if (encoded)
    {
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("& {\r\n" + script + "\r\n} -Quiet")));
    }
    else foreach (var arg in new[] { "-File", scriptPath, "-Quiet" }) info.ArgumentList.Add(arg);
    using var process = Process.Start(info)!;
    var error = process.StandardError.ReadToEnd();
    if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Uninstaller safety check timed out."); }
    if ((process.ExitCode == 0) != shouldSucceed) throw new InvalidOperationException("Unexpected uninstaller exit: " + error.Trim());
    Check(true, shouldSucceed ? "generated uninstaller completed successfully" : "uninstaller refuses an unrecognized folder");
}

try
{
    var optionsType = assembly.GetType("FusionNet.Setup.SetupOptions", true)!;
    var options = Activator.CreateInstance(optionsType)!;
    var retryDirectory = Path.Combine(root, "Fusion [test] & user's app");
    optionsType.GetProperty("InstallDirectory")!.SetValue(options, retryDirectory);
    optionsType.GetProperty("CreateDesktopShortcut")!.SetValue(options, false);
    optionsType.GetProperty("LaunchAfterInstall")!.SetValue(options, false);
    var elevation = assembly.GetType("FusionNet.Setup.SetupElevation", true)!;
    var retry = (ProcessStartInfo)elevation.GetMethod("BuildStartInfo")!.Invoke(null,
        [Path.Combine(root, "setup test.exe"), options])!;
    Check(retry.UseShellExecute && retry.Verb == "runas", "retry requests Windows UAC rather than changing folder permissions");
    var parsed = optionsType.GetMethod("Parse")!.Invoke(null, [retry.ArgumentList.ToArray()])!;
    Check((string?)optionsType.GetProperty("InstallDirectory")!.GetValue(parsed) == retryDirectory,
        "elevated retry preserves literal install path");
    Check(!(bool)optionsType.GetProperty("CreateDesktopShortcut")!.GetValue(parsed)! &&
        !(bool)optionsType.GetProperty("LaunchAfterInstall")!.GetValue(parsed)!, "retry preserves shortcut and launch choices");
    Check(!(bool)optionsType.GetProperty("Silent")!.GetValue(parsed)!, "retry shows wizard again instead of silently installing");
    Check(Rejects(Path.GetPathRoot(root)!), "drive roots are rejected");
    Check(Rejects((string)paths.GetProperty("UserDataDirectory")!.GetValue(null)!), "settings and recovery journal folder is rejected");
    var unsafeFolder = Path.Combine(root, "unrelated");
    Directory.CreateDirectory(unsafeFolder);
    var document = Path.Combine(unsafeFolder, "document.txt");
    File.WriteAllText(document, "keep me");
    Check(Rejects(unsafeFolder) && File.ReadAllText(document) == "keep me", "nonempty unrelated folder is rejected without altering files");
    File.WriteAllText(Path.Combine(unsafeFolder, "README.txt"), "unrelated readme");
    RemoveOwnedFiles(unsafeFolder, shouldSucceed: false, encoded: true);
    Check(File.ReadAllText(Path.Combine(unsafeFolder, "README.txt")) == "unrelated readme", "uninstall refuses to remove unrelated named files without ownership evidence");

    var install = Path.Combine(root, "Fusion [test] %TEMP% & user's app");
    Directory.CreateDirectory(install);
    Check((string)validate.Invoke(null, [install])! == install, "empty dedicated folder is accepted");
    foreach (var name in owned) File.WriteAllText(Path.Combine(install, name), "owned test file");
    File.WriteAllText(Path.Combine(install, "my-note.txt"), "preserve this");
    Directory.CreateDirectory(Path.Combine(install, "my-folder"));
    File.WriteAllText(Path.Combine(install, "my-folder", "save.txt"), "preserve nested file");
    Check((string)validate.Invoke(null, [install])! == install, "existing installation can be upgraded with unrelated files present");
    RemoveOwnedFiles(install);
    Check(owned.All(name => !File.Exists(Path.Combine(install, name))), "all owned program files, including running script, were removed");
    Check(File.ReadAllText(Path.Combine(install, "my-note.txt")) == "preserve this" &&
        File.ReadAllText(Path.Combine(install, "my-folder", "save.txt")) == "preserve nested file", "uninstall preserves unrelated files and subfolders with literal special-character path");

    var cleanInstall = Path.Combine(root, "clean");
    Directory.CreateDirectory(cleanInstall);
    foreach (var name in owned) File.WriteAllText(Path.Combine(cleanInstall, name), "owned test file");
    RemoveOwnedFiles(cleanInstall, encoded: true);
    Check(!Directory.Exists(cleanInstall), "empty installation folder is removed");
    var lockedInstall = Path.Combine(root, "shell-working-directory");
    Directory.CreateDirectory(lockedInstall);
    foreach (var name in owned) File.WriteAllText(Path.Combine(lockedInstall, name), "owned test file");
    RemoveOwnedFiles(lockedInstall, encoded: true, holdWorkingDirectory: true);
    Check(owned.All(name => !File.Exists(Path.Combine(lockedInstall, name))), "a shell holding the install folder does not prevent uninstall completion");
    var runningInstall = Path.Combine(root, "running-app");
    Directory.CreateDirectory(runningInstall);
    foreach (var name in owned) File.WriteAllText(Path.Combine(runningInstall, name), "owned test file");
    // An open handle stands in for an elevated app whose exe this script can neither read nor stop.
    using (new FileStream(Path.Combine(runningInstall, "FusionNetPro.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        RemoveOwnedFiles(runningInstall, shouldSucceed: false, encoded: true);
    Check(owned.All(name => File.Exists(Path.Combine(runningInstall, name))), "uninstall stops before deleting anything while the app exe is in use");

    var shortcuts = assembly.GetType("FusionNet.Setup.ShortcutService", true)!;
    var lnk = Path.Combine(root, "shortcut test", "FusionNet Pro.lnk");
    var target = Environment.ProcessPath!;
    var create = shortcuts.GetMethod("CreateShortcut")!;
    bool RunsAsAdmin() => (BitConverter.ToUInt32(File.ReadAllBytes(lnk), 0x14) & 0x2000) != 0;
    create.Invoke(null, [lnk, target, root, "test"]);
    Check(RunsAsAdmin(), "new shortcut is marked Run as administrator");
    create.Invoke(null, [lnk, target, root, "test"]);
    var runAs = shortcuts.GetMethod("SetRunAsAdministrator")!;
    runAs.Invoke(null, [lnk]);
    Check(RunsAsAdmin(), "upgrade over an existing shortcut and a repeated patch keep the flag");
    dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
    Check(string.Equals((string)wsh.CreateShortcut(lnk).TargetPath, target, StringComparison.OrdinalIgnoreCase), "patched shortcut still opens with its original target");
    var notLink = Path.Combine(root, "not-a-shortcut.lnk");
    File.WriteAllText(notLink, "plain text that is long enough to pass the size test for a shell link header......");
    Check(Throws(() => runAs.Invoke(null, [notLink])) && File.ReadAllText(notLink).StartsWith("plain text"), "a file without a shell link header is refused unchanged");
    Console.WriteLine($"Installer safety: {checks} checks passed.");
    return 0;
}
finally
{
    var expectedParent = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!Path.GetFullPath(root).StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
    Directory.Delete(root, true);
}
