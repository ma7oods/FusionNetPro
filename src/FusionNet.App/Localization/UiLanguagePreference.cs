using System.IO;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Localization;

/// <summary>
/// App-owned persistence for the UI language (Arabic by default, English toggle).
/// <para>
/// The frozen Core <c>FusionSettings.Normalize()</c> currently pins <c>Language</c> to "en",
/// so <c>Settings.Language</c> cannot round-trip a user's Arabic choice. This tiny preference
/// file (in the same data folder) keeps the language independent of that contract and is the
/// source of truth at startup; the settings value is still updated for compatibility.
/// </para>
/// </summary>
public static class UiLanguagePreference
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string DefaultLanguage = Arabic;

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "ui-language.txt");

    /// <summary>Returns the persisted UI language, falling back to the product default (Arabic).</summary>
    public static string Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return DefaultLanguage;

            var text = File.ReadAllText(FilePath).Trim();
            if (string.Equals(text, Arabic, StringComparison.OrdinalIgnoreCase)) return Arabic;
            if (string.Equals(text, English, StringComparison.OrdinalIgnoreCase)) return English;
        }
        catch (IOException)
        {
            // Missing/locked preference file: use the default, never fail startup over it.
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only profile: use the default.
        }

        return DefaultLanguage;
    }

    /// <summary>Persists a normalized language code; best effort (never throws).</summary>
    public static void Save(string? language)
    {
        try
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(FilePath, Normalize(language) + Environment.NewLine);
        }
        catch (IOException)
        {
            // Settings are a convenience: a locked file must not break the UI.
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only profile: the preference simply will not survive a restart.
        }
    }

    /// <summary>Maps any input to <c>"ar"</c> or <c>"en"</c>; the default is Arabic.</summary>
    public static string Normalize(string? language) =>
        string.Equals(language?.Trim(), Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;
}
