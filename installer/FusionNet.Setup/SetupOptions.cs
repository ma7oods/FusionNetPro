using System;
using System.Collections.Generic;

namespace FusionNet.Setup;

/// <summary>Command line options for the FusionNet Pro installer.</summary>
internal sealed class SetupOptions
{
    public bool Silent { get; init; }
    public bool Uninstall { get; init; }
    public bool ShowHelp { get; init; }
    public bool CreateDesktopShortcut { get; init; } = true;
    public bool LaunchAfterInstall { get; init; } = true;
    public string? InstallDirectory { get; init; }
    /// <summary>QA only: render the wizard to a PNG and exit.</summary>
    public string? PreviewPath { get; init; }

    public const string HelpText =
        "FusionNet Pro 3.0.1 — Setup\r\n\r\n" +
        "Usage:\r\n" +
        "  FusionNetPro-Setup.exe                 Install with the graphical wizard\r\n" +
        "  FusionNetPro-Setup.exe /S              Silent install (current user)\r\n" +
        "  FusionNetPro-Setup.exe /S /D=<path>    Silent install into a custom folder\r\n" +
        "  FusionNetPro-Setup.exe /S /nodesktop   Silent install without desktop shortcut\r\n" +
        "  FusionNetPro-Setup.exe /S /launch      Silent install and start the app\r\n" +
        "  FusionNetPro-Setup.exe /uninstall      Uninstall (!quiet shows a confirmation)\r\n" +
        "  FusionNetPro-Setup.exe /uninstall /quiet   Remove silently\r\n\r\n" +
        "الاستخدام بالعربية:\r\n" +
        "  انقر نقراً مزدوجاً على الملف ثم اتبع المعالج.\r\n" +
        "  /S للتثبيت الصامت، و /uninstall للإزالة.";

    public static SetupOptions Parse(IReadOnlyList<string> args)
    {
        bool silent = false, uninstall = false, help = false, desktop = true, launch = true, launchSpecified = false, quiet = false;
        string? dir = null, preview = null;

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var token = raw.Trim();
            if (token.StartsWith("\"", StringComparison.Ordinal) && token.EndsWith("\"", StringComparison.Ordinal) && token.Length > 1)
                token = token[1..^1];

            var normalized = token.TrimStart('/', '-').Trim();
            if (normalized.Length == 0) continue;

            var eq = normalized.IndexOf('=');
            var name = (eq >= 0 ? normalized[..eq] : normalized).Trim().ToLowerInvariant();
            var value = eq >= 0 ? normalized[(eq + 1)..].Trim().Trim('"') : string.Empty;

            switch (name)
            {
                case "s":
                case "silent":
                case "quiet":
                    silent = true;
                    quiet = true;
                    break;
                case "uninstall":
                case "remove":
                    uninstall = true;
                    break;
                case "d":
                case "dir":
                case "directory":
                case "install":
                    if (value.Length > 0) dir = value;
                    break;
                case "nodesktop":
                case "no-desktop":
                    desktop = false;
                    break;
                case "nolaunch":
                case "no-launch":
                    launch = false;
                    launchSpecified = true;
                    break;
                case "launch":
                    launch = true;
                    launchSpecified = true;
                    break;
                case "preview":
                    preview = value;
                    break;
                case "h":
                case "help":
                case "?":
                    help = true;
                    break;
            }
        }

        if (uninstall && !quiet) silent = false;
        // Silent installs do not launch the app unless /launch was passed explicitly.
        if (silent && !launchSpecified) launch = false;

        return new SetupOptions
        {
            Silent = silent,
            Uninstall = uninstall,
            ShowHelp = help,
            CreateDesktopShortcut = desktop,
            LaunchAfterInstall = launch,
            InstallDirectory = dir,
            PreviewPath = preview
        };
    }
}
