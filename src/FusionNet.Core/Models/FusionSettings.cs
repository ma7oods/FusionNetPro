using System.Text.RegularExpressions;
using FusionNet.Core.Utilities;

namespace FusionNet.Core.Models;

/// <summary>
/// Every user preference in one serialisable document. <see cref="Normalize"/> makes any
/// loaded file safe, self-healing corrupted values instead of throwing.
/// </summary>
public sealed class FusionSettings
{
    public const int CurrentSchemaVersion = 3;

    // ---------------------------------------------------------------- Appearance
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Language { get; set; } = "en";
    public string Accent { get; set; } = "cyan";

    // ---------------------------------------------------------------- Behaviour
    public bool SimpleMode { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool ConfirmExitWhileActive { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+F";
    public bool AutoEngageOnGameStart { get; set; }
    public bool RestoreOnGameExit { get; set; } = true;
    public bool AutoRestoreOnCrash { get; set; } = true;
    public bool HasCompletedSetup { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
    public int SessionCount { get; set; }

    // ---------------------------------------------------------------- Fusion mode scope
    public bool EnableFirewallBlocking { get; set; } = true;
    public bool PauseWindowsDownloadServices { get; set; } = true;
    public List<string> PausedServiceIds { get; set; } = [.. ServiceCatalog.DefaultPausedIds];
    public bool BoostGameProcess { get; set; } = true;
    public bool SwitchPowerPlan { get; set; } = true;
    public bool ApplyLatencyTweaks { get; set; } = true;
    public List<string> EnabledTweakIds { get; set; } = [.. TweakCatalog.RecommendedIds];
    public bool FlushDnsOnEngage { get; set; }
    public bool PurgeStandbyMemoryOnEngage { get; set; }
    public bool SuspendAppsOnlyWhileGameRuns { get; set; } = true;

    // ---------------------------------------------------------------- Rules
    public Dictionary<string, AppControlAction> AppActions { get; set; } = ManagedAppCatalog.DefaultActions();
    public List<CustomManagedApp> CustomApps { get; set; } = [];

    // ---------------------------------------------------------------- Games
    public string SelectedGameId { get; set; } = GameCatalog.AutoProfileId;
    public List<CustomGame> CustomGames { get; set; } = [];

    // ---------------------------------------------------------------- Monitoring
    public List<string> PingTargets { get; set; } = ["1.1.1.1", "8.8.8.8"];
    public int PingIntervalMs { get; set; } = 1000;
    public int PingTimeoutMs { get; set; } = 1000;
    public bool LiveMonitoring { get; set; } = true;

    // ---------------------------------------------------------------- DNS
    public string? SelectedAdapterId { get; set; }
    public List<string> CustomDnsServers { get; set; } = [];
    public bool AutoSelectFastestDns { get; set; }

    public static FusionSettings CreateDefault() => new FusionSettings().Normalize();

    /// <summary>Repairs null/out-of-range values so the rest of the app can trust the object.</summary>
    public FusionSettings Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;

        // English-only build (user request): ignore any stored language value.
        Language = "en";
        Accent = CanonicalAccent(Accent);

        if (!HotkeyGesture.TryParse(Hotkey, out var hotkey) || hotkey.ToVirtualKey() == 0)
            Hotkey = HotkeyGesture.Default.ToString();
        else
            Hotkey = hotkey.ToString();

        AppActions ??= new Dictionary<string, AppControlAction>(StringComparer.OrdinalIgnoreCase);
        var cleanActions = new Dictionary<string, AppControlAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var (appId, action) in AppActions)
        {
            if (string.IsNullOrWhiteSpace(appId) || !action.IsEnforced()) continue;
            if (ManagedAppCatalog.Find(appId) is null) continue;
            cleanActions[appId] = action;
        }
        AppActions = cleanActions;

        CustomApps ??= [];
        var cleanApps = new List<CustomManagedApp>(CustomApps.Count);
        var seenAppPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in CustomApps)
        {
            if (app is null) continue;

            var executablePath = TryNormalizeExecutablePath(app.ExecutablePath);
            if (executablePath is null || !seenAppPaths.Add(executablePath)) continue;

            cleanApps.Add(new CustomManagedApp(
                string.IsNullOrWhiteSpace(app.Id) ? CustomManagedApp.BuildId(executablePath) : app.Id.Trim(),
                string.IsNullOrWhiteSpace(app.Name) ? Path.GetFileNameWithoutExtension(executablePath) : app.Name.Trim(),
                executablePath,
                app.Action == AppControlAction.Ignore ? AppControlAction.BlockNetwork : app.Action,
                app.Enabled));
        }

        CustomApps = cleanApps;

        CustomGames ??= [];
        var cleanGames = new List<CustomGame>(CustomGames.Count);
        var seenGamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in CustomGames)
        {
            if (game is null) continue;

            var executablePath = TryNormalizeExecutablePath(game.ExecutablePath);
            if (executablePath is null || !seenGamePaths.Add(executablePath)) continue;

            cleanGames.Add(game with
            {
                Id = string.IsNullOrWhiteSpace(game.Id) ? CustomGame.Create(executablePath).Id : game.Id.Trim(),
                Name = string.IsNullOrWhiteSpace(game.Name)
                    ? Path.GetFileNameWithoutExtension(executablePath)
                    : game.Name.Trim(),
                ExecutablePath = executablePath
            });
        }

        CustomGames = cleanGames;

        var gameId = SelectedGameId?.Trim();
        var selectedBuiltIn = GameCatalog.Find(gameId);
        var selectedCustom = CustomGames.FirstOrDefault(game =>
            string.Equals(game.Id, gameId, StringComparison.OrdinalIgnoreCase));

        SelectedGameId = selectedBuiltIn is not null ? selectedBuiltIn.Id
            : selectedCustom is { Enabled: true } ? selectedCustom.Id
            : GameCatalog.AutoProfileId;

        PausedServiceIds ??= [];
        PausedServiceIds = PausedServiceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => ServiceCatalog.Find(id)?.Id ?? id)
            .Where(id => ServiceCatalog.Find(id) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnabledTweakIds ??= [];
        EnabledTweakIds = EnabledTweakIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => TweakCatalog.Find(id)?.Id ?? id)
            .Where(id => TweakCatalog.Find(id) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        PingTargets ??= [];
        PingTargets = PingTargets
            .Where(IsValidHost)
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (PingTargets.Count == 0) PingTargets = ["1.1.1.1", "8.8.8.8"];

        PingIntervalMs = Math.Clamp(PingIntervalMs, 500, 10_000);
        PingTimeoutMs = Math.Clamp(PingTimeoutMs, 250, 5_000);

        CustomDnsServers ??= [];
        CustomDnsServers = CustomDnsServers
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Select(server => server.Trim())
            .Where(DnsCatalog.IsValidIPv4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (string.IsNullOrWhiteSpace(SelectedAdapterId)) SelectedAdapterId = null;
        if (SessionCount < 0) SessionCount = 0;

        return this;
    }

    public FusionSettings Clone()
    {
        var clone = FusionJson.Deserialize<FusionSettings>(FusionJson.Serialize(this)) ?? CreateDefault();

        // JSON dictionaries are re-created with the default case-sensitive comparer; the settings
        // contract is case-insensitive app-id lookup, so restore it on the copy.
        var actions = new Dictionary<string, AppControlAction>(StringComparer.OrdinalIgnoreCase);
        if (clone.AppActions is not null)
        {
            foreach (var (appId, action) in clone.AppActions) actions[appId] = action;
        }

        clone.AppActions = actions;
        return clone;
    }

    public AppControlAction GetAction(string? appId) =>
        AppActions is not null && !string.IsNullOrWhiteSpace(appId) &&
        AppActions.TryGetValue(appId, out var action)
            ? action
            : AppControlAction.Ignore;

    public void SetAction(string appId, AppControlAction action)
    {
        if (string.IsNullOrWhiteSpace(appId)) return;
        if (action.IsEnforced()) AppActions[appId] = action;
        else AppActions.Remove(appId);
    }

    public bool IsTweakEnabled(string tweakId) =>
        ApplyLatencyTweaks && EnabledTweakIds.Contains(tweakId, StringComparer.OrdinalIgnoreCase);

    public bool IsServicePaused(string serviceId) =>
        PauseWindowsDownloadServices && PausedServiceIds.Contains(serviceId, StringComparer.OrdinalIgnoreCase);

    private static string CanonicalAccent(string? accent)
    {
        var trimmed = accent?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return AccentCatalog.DefaultId;
        foreach (var id in AccentCatalog.All)
        {
            if (string.Equals(id, trimmed, StringComparison.OrdinalIgnoreCase)) return id;
        }

        return AccentCatalog.DefaultId;
    }

    /// <summary>
    /// Fully-qualified path for a persisted executable path, or <c>null</c> when the stored value is
    /// unusable. Persisted paths are untrusted input: a corrupted settings file must be repaired,
    /// never throw (<see cref="Path.GetFullPath(string)"/> and <see cref="Path.GetExtension(string)"/>
    /// throw <see cref="ArgumentException"/> for illegal characters such as NUL).
    /// </summary>
    private static string? TryNormalizeExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var trimmed = path.Trim();
            if (!string.Equals(Path.GetExtension(trimmed), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool IsValidHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length > 253) return false;
        return Regex.IsMatch(trimmed, @"^[A-Za-z0-9](?:[A-Za-z0-9\-\._:]{0,251}[A-Za-z0-9])?$");
    }
}

/// <summary>The five accent colours the UI offers (ids are persisted in settings).</summary>
public static class AccentCatalog
{
    public const string DefaultId = "cyan";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "cyan", "violet", "emerald", "amber", "rose" });

    public static bool IsKnown(string? id)
    {
        var trimmed = id?.Trim();
        return !string.IsNullOrEmpty(trimmed) && All.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }
}
