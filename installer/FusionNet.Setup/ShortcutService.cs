using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FusionNet.Setup;

/// <summary>Creates .lnk files through the WScript.Shell COM object (works per-user, no admin).
/// Each shortcut is marked "Run as administrator" because Fusion Mode needs elevation.</summary>
internal static class ShortcutService
{
    private const int LinkFlagsOffset = 0x14;
    private const uint RunAsUser = 0x2000;
    public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        var folder = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available on this system.");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            dynamic shellObject = shell;
            shortcut = shellObject.CreateShortcut(shortcutPath);
            dynamic link = shortcut;
            link.TargetPath = targetPath;
            link.WorkingDirectory = workingDirectory;
            link.IconLocation = targetPath + ",0";
            link.Description = description;
            link.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }

        SetRunAsAdministrator(shortcutPath);
    }

    // [MS-SHLLINK] 2.1: the header is 0x4C bytes and LinkFlags is the uint32 at 0x14; RunAsUser is the
    // "Run as administrator" checkbox. WScript.Shell cannot set it, so patch the saved file.
    public static void SetRunAsAdministrator(string shortcutPath)
    {
        var bytes = File.ReadAllBytes(shortcutPath);
        if (bytes.Length < 0x4C || BitConverter.ToUInt32(bytes, 0) != 0x4C)
            throw new InvalidDataException("Unexpected shortcut header: " + shortcutPath);
        var flags = BitConverter.ToUInt32(bytes, LinkFlagsOffset);
        if ((flags & RunAsUser) != 0) return;
        BitConverter.GetBytes(flags | RunAsUser).CopyTo(bytes, LinkFlagsOffset);
        File.WriteAllBytes(shortcutPath, bytes);
    }

    public static void RemoveShortcut(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
        }
        catch
        {
            // best effort
        }
    }
}
