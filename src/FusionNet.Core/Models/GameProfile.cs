namespace FusionNet.Core.Models;

/// <summary>A game that FusionNet can auto-detect and protect.</summary>
public sealed record GameProfile(
    string Id,
    string Name,
    string Category,
    IReadOnlyList<string> ProcessNames,
    string? SteamAppId = null)
{
    public bool MatchesProcess(string processName) =>
        ProcessNames.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));

    public bool MatchesAnyProcess(IEnumerable<string> processNames) =>
        processNames.Any(MatchesProcess);

    public override string ToString() => Name;
}

/// <summary>A custom game added by the user (any .exe).</summary>
public sealed record CustomGame(
    string Id,
    string Name,
    string ExecutablePath,
    bool Enabled = true)
{
    public string ProcessName => Path.GetFileNameWithoutExtension(ExecutablePath);

    public static CustomGame Create(string executablePath)
    {
        var full = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The game must be a Windows executable (.exe).", nameof(executablePath));

        var process = Path.GetFileNameWithoutExtension(full);
        if (string.IsNullOrWhiteSpace(process))
            throw new ArgumentException("The game executable must have a valid file name.", nameof(executablePath));

        var id = "custom-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..12].ToLowerInvariant();

        return new CustomGame(id, process, full);
    }

    public GameProfile ToProfile() =>
        new(Id, Name, "Custom", Array.AsReadOnly(new[] { ProcessName }), null);
}

public static class GameCatalog
{
    public static bool IsRiotSupportProcess(string processName) =>
        RiotSupportProcesses.Contains(processName);

    private static readonly HashSet<string> RiotSupportProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "riotclientservices", "riotclientux", "riotclientuxrender", "vgc", "vgtray"
    };

    public const string AutoProfileId = "auto";

    private static readonly GameProfile[] Profiles = CreateProfiles();

    private static GameProfile[] CreateProfiles()
    {
        GameProfile[] profiles =
        [
            new("valorant", "VALORANT", "Tactical FPS", ["VALORANT-Win64-Shipping", "VALORANT"], "1203220"),
            new("counter-strike-2", "Counter-Strike 2", "Tactical FPS", ["cs2", "csgo"], "730"),
            new("apex-legends", "Apex Legends", "Battle Royale", ["r5apex", "r5apex_dx12"], "1172470"),
            new("fortnite", "Fortnite", "Battle Royale", ["FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_EAC", "FortniteClient-Win64-Shipping_BE"], null),
            new("pubg", "PUBG: BATTLEGROUNDS", "Battle Royale", ["TslGame", "TslGame_BE"], "578080"),
            new("warzone", "Call of Duty: Warzone", "Battle Royale", ["cod", "ModernWarfare", "sp23-cod", "sp24-cod"], null),
            new("league-of-legends", "League of Legends", "MOBA", ["League of Legends", "LeagueClientUx"], "20590"),
            new("dota-2", "Dota 2", "MOBA", ["dota2"], "570"),
            new("overwatch-2", "Overwatch 2", "Hero Shooter", ["Overwatch"], "2357570"),
            new("marvel-rivals", "Marvel Rivals", "Hero Shooter", ["MarvelRivals-Win64-Shipping"], null),
            new("rainbow-six-siege", "Rainbow Six Siege", "Tactical FPS", ["RainbowSix", "RainbowSix_BE", "RainbowSix_DX11"], "359550"),
            new("delta-force", "Delta Force", "Tactical FPS", ["DeltaForceClient-Win64-Shipping"], null),
            new("rust", "Rust", "Survival", ["RustClient", "Rust"], "252490"),
            new("escape-from-tarkov", "Escape from Tarkov", "Extraction", ["EscapeFromTarkov", "EscapeFromTarkov_BE"], null),
            new("gta-v", "Grand Theft Auto V", "Open World", ["GTA5", "GTA5_Enhanced"], "271590"),
            new("fivem", "FiveM (GTA RP)", "Open World", ["FiveM", "FiveM_GTAProcess"], null),
            new("rocket-league", "Rocket League", "Sports", ["RocketLeague"], "252950"),
            new("ea-fc", "EA SPORTS FC", "Sports", ["FC24", "FC25", "FC26"], null),
            new("destiny-2", "Destiny 2", "Looter Shooter", ["destiny2"], "1085660"),
            new("halo-infinite", "Halo Infinite", "Arena FPS", ["HaloInfinite"], "1240440"),
            new("battlefield-2042", "Battlefield 2042", "Arena FPS", ["Battlefield2042"], "1517290"),
            new("roblox", "Roblox", "Sandbox", ["RobloxPlayerBeta"], null)
        ];

        // The catalog is global state: expose immutable per-game process lists so no caller can mutate it.
        return profiles
            .Select(profile => profile with { ProcessNames = Array.AsReadOnly(profile.ProcessNames.ToArray()) })
            .ToArray();
    }

    public static IReadOnlyList<GameProfile> All { get; } = Array.AsReadOnly(Profiles);

    public static IReadOnlyList<string> Categories { get; } =
        Array.AsReadOnly(Profiles.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    public static GameProfile? Find(string? id) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>All built-in games plus enabled custom games.</summary>
    public static IReadOnlyList<GameProfile> BuildWatchList(FusionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var custom = settings.CustomGames
            .Where(game => game is { Enabled: true } && !string.IsNullOrWhiteSpace(game.ExecutablePath))
            .Select(game => game.ToProfile())
            .ToArray();

        if (string.Equals(settings.SelectedGameId, AutoProfileId, StringComparison.OrdinalIgnoreCase))
            return Profiles.Concat(custom).ToArray();

        var selectedBuiltIn = Find(settings.SelectedGameId);
        if (selectedBuiltIn is not null) return [selectedBuiltIn, .. custom];

        // A pinned custom game keeps all custom games available; an unknown id must not disable
        // built-in detection, so it behaves like the automatic watch list.
        var selectedCustom = custom.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.SelectedGameId, StringComparison.OrdinalIgnoreCase));
        return selectedCustom is not null ? custom : Profiles.Concat(custom).ToArray();
    }

    /// <summary>Finds the first running game; when several match, the pinned profile wins.</summary>
    public static GameProfile? DetectRunning(IReadOnlyList<GameProfile> watchList, IEnumerable<string> runningProcessNames, string? pinnedGameId)
    {
        ArgumentNullException.ThrowIfNull(watchList);
        ArgumentNullException.ThrowIfNull(runningProcessNames);

        var names = runningProcessNames as ICollection<string> ?? runningProcessNames.ToArray();
        var matches = watchList.Where(profile => profile.MatchesAnyProcess(names)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length == 1) return matches[0];

        return matches.FirstOrDefault(m => string.Equals(m.Id, pinnedGameId, StringComparison.OrdinalIgnoreCase))
               ?? matches[0];
    }
}
