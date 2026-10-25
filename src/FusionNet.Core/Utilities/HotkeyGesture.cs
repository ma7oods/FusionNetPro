using System.Globalization;

namespace FusionNet.Core.Utilities;

/// <summary>Application data locations. Everything FusionNet persists lives here.</summary>
public static class AppPaths
{
    public const string ProductName = "FusionNet Pro";
    public const string Slug = "FusionNetPro";
    public const string ExecutableName = "FusionNetPro.exe";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Slug);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string JournalFile => Path.Combine(DataDirectory, "journal.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string ReportDirectory => Path.Combine(DataDirectory, "reports");
    public static string CrashDirectory => Path.Combine(DataDirectory, "crashes");
    public static string ExportedSettingsDirectory => Path.Combine(DataDirectory, "backups");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ReportDirectory);
        Directory.CreateDirectory(CrashDirectory);
        Directory.CreateDirectory(ExportedSettingsDirectory);
    }
}

/// <summary>A parsed global hotkey such as Ctrl+Alt+F.</summary>
public sealed record HotkeyGesture(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    public static HotkeyGesture Default => new(true, true, false, false, "F");

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    public string DisplayString => string.Join(" + ", ModifierParts().Append(Key));

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        var ctrl = false;
        var alt = false;
        var shift = false;
        var win = false;
        string? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "windows":
                case "meta":
                    win = true;
                    break;
                default:
                    if (key is not null) return false;
                    key = NormalizeKey(part);
                    if (key is null) return false;
                    break;
            }
        }

        if (key is null || (!ctrl && !alt && !shift && !win)) return false;

        gesture = new HotkeyGesture(ctrl, alt, shift, win, key);
        return true;
    }

    public override string ToString() => string.Join("+", ModifierParts().Append(Key));

    public IReadOnlyList<string> ModifierParts()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        return parts;
    }

    /// <summary>Win32 virtual-key code for the main key (letters, digits, F1-F12).</summary>
    public uint ToVirtualKey()
    {
        if (Key.Length == 1)
        {
            var c = char.ToUpperInvariant(Key[0]);
            if (c is >= 'A' and <= 'Z') return (uint)c;
            if (c is >= '0' and <= '9') return (uint)c;
        }

        if (Key.Length is >= 2 and <= 3 && (Key[0] is 'F' or 'f') &&
            int.TryParse(Key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number is >= 1 and <= 12)
        {
            return (uint)(0x70 + (number - 1));
        }

        return 0;
    }

    private static string? NormalizeKey(string raw)
    {
        var key = raw.Trim();

        // Only ASCII letters/digits can be mapped to a Win32 virtual key; accepting other Unicode
        // letters or Arabic-Indic digits would parse a gesture that can never be registered.
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return key.ToUpperInvariant();
        if (key.Length is >= 2 and <= 3 && (key[0] is 'F' or 'f') &&
            int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number is >= 1 and <= 12)
        {
            return "F" + number;
        }

        return key.ToLowerInvariant() switch
        {
            "space" => "Space",
            "tab" => "Tab",
            "insert" => "Insert",
            "delete" => "Delete",
            "home" => "Home",
            "end" => "End",
            "pageup" => "PageUp",
            "pagedown" => "PageDown",
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            _ => null
        };
    }
}
