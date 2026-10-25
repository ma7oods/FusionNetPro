using System.Diagnostics;
using System.Text.Json;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Windows.System;

/// <summary>
/// Persists <see cref="FusionSettings"/> as one JSON document at <see cref="AppPaths.SettingsFile"/>.
/// A corrupt file is quarantined instead of crashing the app, and every save is normalised first.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly object _gate = new();

    public string FilePath => AppPaths.SettingsFile;

    /// <summary>Human readable description of the last load/save problem, or null.</summary>
    public string? LastError { get; private set; }

    public FusionSettings Load()
    {
        lock (_gate)
        {
            try
            {
                AppPaths.EnsureDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = "Could not create the data folder: " + ex.Message;
            }

            string json;
            try
            {
                if (!File.Exists(FilePath))
                {
                    LastError = null;
                    var defaults = FusionSettings.CreateDefault();
                    TrySave(defaults);
                    return defaults;
                }

                json = File.ReadAllText(FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file exists but cannot be read right now; keep it and fall back in memory.
                LastError = "Could not read settings: " + ex.Message;
                return FusionSettings.CreateDefault();
            }

            try
            {
                var loaded = FusionJson.Deserialize<FusionSettings>(json);
                if (loaded is null)
                {
                    LastError = "Settings file is empty or not a FusionNet settings document.";
                    Quarantine();
                    return RecreateDefaults();
                }

                LastError = null;
                return loaded.Normalize();
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                LastError = "Settings file was corrupted and has been quarantined: " + ex.Message;
                Quarantine();
                return RecreateDefaults();
            }
        }
    }

    public void Save(FusionSettings settings)
    {
        if (settings is null) return;

        lock (_gate)
        {
            TrySave(settings);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            TrySave(FusionSettings.CreateDefault());
        }
    }

    private FusionSettings RecreateDefaults()
    {
        var defaults = FusionSettings.CreateDefault();
        TrySave(defaults);
        return defaults;
    }

    private void TrySave(FusionSettings settings)
    {
        try
        {
            AppPaths.EnsureDirectories();
            FusionJson.SaveAtomic(FilePath, settings.Normalize());
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Settings are a convenience: never let a locked/disallowed file take the app down.
            LastError = "Could not save settings: " + ex.Message;
            Debug.WriteLine(LastError);        }
    }

    /// <summary>Moves a corrupt file aside so support can inspect it and a good file can be written.</summary>
    private void Quarantine()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(AppPaths.DataDirectory, $"settings.corrupt-{stamp}.json");
            var suffix = 1;
            while (File.Exists(target))
            {
                target = Path.Combine(AppPaths.DataDirectory, $"settings.corrupt-{stamp}-{suffix++}.json");
            }

            File.Move(FilePath, target, overwrite: false);
            Debug.WriteLine("Quarantined corrupt settings file to " + target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Quarantine is best effort; the caller still gets working defaults.
            Debug.WriteLine("Settings quarantine failed: " + ex.Message);
        }
    }
}
