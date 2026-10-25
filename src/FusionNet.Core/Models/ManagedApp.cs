namespace FusionNet.Core.Models;

/// <summary>A background application FusionNet can block, suspend or throttle while you play.</summary>
public sealed record ManagedApp(
    string Id,
    string Name,
    string Category,
    string Description,
    IReadOnlyList<string> ProcessNames,
    AppControlAction DefaultAction,
    bool Recommended,
    IReadOnlyList<string>? KnownPaths = null)
{
    public IReadOnlyList<string> Paths => KnownPaths ?? [];

    public bool MatchesProcess(string processName) =>
        ProcessNames.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));

    public bool MatchesAnyProcess(IEnumerable<string> processNames) =>
        processNames.Any(MatchesProcess);

    public override string ToString() => Name;
}

public static class ManagedAppCatalog
{
    public const string Launchers = "Launchers";
    public const string CloudSync = "Cloud sync";
    public const string Torrents = "Torrent clients";
    public const string Downloads = "Download managers";
    public const string Browsers = "Web browsers";
    public const string UpdateAgents = "Update agents";
    public const string RemoteTools = "Remote tools";

    private static readonly ManagedApp[] Apps = CreateApps();

    private static ManagedApp[] CreateApps()
    {
        ManagedApp[] apps =
        [
        // ---- Game launchers and their download workers -------------------------
        new("steam", "Steam", Launchers, "Launcher, workshop and game downloads",
            ["steam", "steamwebhelper", "steamservice", "steamerrorreporter"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles(x86)%\Steam\steam.exe", @"%ProgramFiles%\Steam\steam.exe"]),

        new("epic", "Epic Games Launcher", Launchers, "Launcher and Unreal Engine downloads",
            ["EpicGamesLauncher", "EpicWebHelper", "EpicOnlineServicesHost"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles(x86)%\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe",
             @"%ProgramFiles%\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"]),

        new("battlenet", "Battle.net", Launchers, "Blizzard launcher and update agent",
            ["Battle.net", "Agent", "BlizzardBrowser"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles(x86)%\Battle.net\Battle.net.exe"]),

        new("ea-app", "EA app", Launchers, "EA desktop client and downloads",
            ["EADesktop", "EAbackgroundservice", "Origin", "OriginWebHelperService"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles%\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe",
             @"%ProgramFiles(x86)%\Origin\Origin.exe"]),

        new("ubisoft", "Ubisoft Connect", Launchers, "Ubisoft launcher and updater",
            ["UbisoftConnect", "upc", "UbisoftGameLauncher"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles(x86)%\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"]),

        new("gog", "GOG Galaxy", Launchers, "GOG launcher and updates",
            ["GalaxyClient", "GalaxyCommunication", "GalaxyUpdater"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles(x86)%\GOG Galaxy\GalaxyClient.exe"]),

        new("amazon-games", "Amazon Games", Launchers, "Prime Gaming launcher",
            ["AmazonGames", "AmazonGamesUi"],
            AppControlAction.Suspend, true,
            [@"%LocalAppData%\Amazon Games\App\AmazonGames.exe"]),

        new("rockstar", "Rockstar Games Launcher", Launchers, "Rockstar launcher and updates",
            ["RockstarErrorHandler", "SocialClubHelper", "Launcher"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles%\Rockstar Games\Launcher\Launcher.exe"]),

        // ---- Cloud sync --------------------------------------------------------
        new("onedrive", "OneDrive", CloudSync, "Microsoft cloud file sync",
            ["OneDrive", "FileCoAuth"],
            AppControlAction.Suspend, true,
            [@"%LocalAppData%\Microsoft\OneDrive\OneDrive.exe", @"%ProgramFiles%\Microsoft OneDrive\OneDrive.exe"]),

        new("google-drive", "Google Drive", CloudSync, "Google Drive file sync",
            ["GoogleDriveFS", "GoogleDriveFSHelper"],
            AppControlAction.Suspend, true),

        new("dropbox", "Dropbox", CloudSync, "Dropbox file sync",
            ["Dropbox", "DropboxUpdate"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles(x86)%\Dropbox\Dropbox.exe", @"%ProgramFiles%\Dropbox\Dropbox.exe"]),

        new("icloud", "iCloud", CloudSync, "Apple iCloud for Windows",
            ["iCloudServices", "iCloudDrive", "iCloudPhotos"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles%\Common Files\Apple\Internet Services\iCloudServices.exe"]),

        new("mega", "MEGA", CloudSync, "MEGA cloud sync",
            ["MEGAsync"],
            AppControlAction.Suspend, true,
            [@"%LocalAppData%\MEGAsync\MEGAsync.exe"]),

        new("pcloud", "pCloud Drive", CloudSync, "pCloud cloud sync",
            ["pCloud"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles%\pCloud Drive\pCloud.exe"]),

        new("nextcloud", "Nextcloud", CloudSync, "Nextcloud desktop sync",
            ["nextcloud"],
            AppControlAction.Suspend, true,
            [@"%ProgramFiles%\Nextcloud\nextcloud.exe"]),

        // ---- Torrent clients ---------------------------------------------------
        new("qbittorrent", "qBittorrent", Torrents, "BitTorrent client - blocks all peer traffic",
            ["qbittorrent"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles%\qBittorrent\qbittorrent.exe", @"%ProgramFiles(x86)%\qBittorrent\qbittorrent.exe"]),

        new("utorrent", "uTorrent", Torrents, "BitTorrent client - blocks all peer traffic",
            ["utorrent"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles(x86)%\uTorrent\uTorrent.exe", @"%LocalAppData%\uTorrent\uTorrent.exe"]),

        new("bittorrent", "BitTorrent", Torrents, "BitTorrent client - blocks all peer traffic",
            ["BitTorrent"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles%\BitTorrent\BitTorrent.exe"]),

        new("transmission", "Transmission", Torrents, "BitTorrent client - blocks all peer traffic",
            ["transmission-qt", "transmission-daemon"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles%\Transmission\transmission-qt.exe"]),

        new("deluge", "Deluge", Torrents, "BitTorrent client - blocks all peer traffic",
            ["deluge"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles(x86)%\Deluge\deluge.exe"]),

        // ---- Download managers -------------------------------------------------
        new("idm", "Internet Download Manager", Downloads, "Multi-threaded download accelerator",
            ["IDMan", "IDManType", "IEMonitor"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles(x86)%\Internet Download Manager\IDMan.exe"]),

        new("fdm", "Free Download Manager", Downloads, "Download accelerator",
            ["fdm", "FDM"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles%\Softdeluxe\Free Download Manager\fdm.exe"]),

        new("jdownloader", "JDownloader", Downloads, "Download manager",
            ["JDownloader2", "javaw"],
            AppControlAction.BlockNetwork, true,
            [@"%ProgramFiles%\JDownloader\JDownloader2.exe"]),

        // ---- Browsers ----------------------------------------------------------
        new("chrome", "Google Chrome", Browsers, "Blocks all browser traffic while the game runs",
            ["chrome"],
            AppControlAction.BlockNetwork, false,
            [@"%ProgramFiles%\Google\Chrome\Application\chrome.exe", @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"]),

        new("edge", "Microsoft Edge", Browsers, "Blocks all browser traffic while the game runs",
            ["msedge"],
            AppControlAction.BlockNetwork, false,
            [@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"]),

        new("firefox", "Mozilla Firefox", Browsers, "Blocks all browser traffic while the game runs",
            ["firefox"],
            AppControlAction.BlockNetwork, false,
            [@"%ProgramFiles%\Mozilla Firefox\firefox.exe"]),

        new("brave", "Brave", Browsers, "Blocks all browser traffic while the game runs",
            ["brave"],
            AppControlAction.BlockNetwork, false,
            [@"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe"]),

        new("opera", "Opera", Browsers, "Blocks all browser traffic while the game runs",
            ["opera"],
            AppControlAction.BlockNetwork, false,
            [@"%LocalAppData%\Programs\Opera\opera.exe", @"%ProgramFiles%\Opera\opera.exe"]),

        // ---- Update agents -----------------------------------------------------
        new("nvidia-app", "NVIDIA app / GeForce Experience", UpdateAgents, "Driver downloads and telemetry",
            ["NVIDIA GeForce Experience", "NVIDIA app", "NVIDIA Share", "NVIDIA Overlay"],
            AppControlAction.Suspend, false,
            [@"%ProgramFiles%\NVIDIA Corporation\NVIDIA GeForce Experience\NVIDIA GeForce Experience.exe",
             @"%ProgramFiles%\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA app.exe"]),

        new("adobe-cc", "Adobe Creative Cloud", UpdateAgents, "Creative Cloud sync and updates",
            ["Creative Cloud", "Adobe Desktop Service", "CoreSync", "AdobeIPCBroker"],
            AppControlAction.Suspend, false,
            [@"%ProgramFiles(x86)%\Adobe\Adobe Creative Cloud\ACC\Creative Cloud.exe"]),

        new("intel-dsa", "Intel Driver & Support Assistant", UpdateAgents, "Driver update downloads",
            ["DSATray", "IntelDriverUpdateUtility"],
            AppControlAction.Suspend, false),

        // ---- Voice and remote tools (protected by default) ---------------------
        new("discord", "Discord", RemoteTools, "Voice chat - throttled instead of blocked so you stay in call",
            ["Discord", "DiscordPTB", "DiscordCanary"],
            AppControlAction.Throttle, true,
            [@"%LocalAppData%\Discord\Update.exe"]),

        new("teamviewer", "TeamViewer", RemoteTools, "Remote desktop traffic",
            ["TeamViewer", "TeamViewer_Service"],
            AppControlAction.Ignore, false,
            [@"%ProgramFiles%\TeamViewer\TeamViewer.exe"]),

        new("anydesk", "AnyDesk", RemoteTools, "Remote desktop traffic",
            ["AnyDesk"],
            AppControlAction.Ignore, false,
            [@"%ProgramFiles(x86)%\AnyDesk\AnyDesk.exe"]),

        new("obs", "OBS Studio", RemoteTools, "Streaming and recording (network heavy if streaming)",
            ["obs64", "obs32"],
            AppControlAction.Ignore, false,
            [@"%ProgramFiles%\obs-studio\bin\64bit\obs64.exe"]),

        new("spotify", "Spotify", RemoteTools, "Music streaming",
            ["Spotify"],
            AppControlAction.Ignore, false,
            [@"%AppData%\Spotify\Spotify.exe"])
        ];

        // The catalog is global state: expose immutable per-app lists so no caller can mutate it.
        return apps
            .Select(app => app with
            {
                ProcessNames = Array.AsReadOnly(app.ProcessNames.ToArray()),
                KnownPaths = app.KnownPaths is null ? null : Array.AsReadOnly(app.KnownPaths.ToArray())
            })
            .ToArray();
    }

    public static IReadOnlyList<ManagedApp> All { get; } = Array.AsReadOnly(Apps);

    public static IReadOnlyList<string> Categories { get; } =
        Array.AsReadOnly(Apps.Select(app => app.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    public static ManagedApp? Find(string? id) =>
        Apps.FirstOrDefault(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ManagedApp? FindByProcessName(string processName) =>
        Apps.FirstOrDefault(app => app.MatchesProcess(processName));

    /// <summary>Pre-selected rules used on a fresh install (recommended apps only).</summary>
    public static Dictionary<string, AppControlAction> DefaultActions()
    {
        var actions = new Dictionary<string, AppControlAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in Apps.Where(a => a.Recommended))
            actions[app.Id] = app.DefaultAction;
        return actions;
    }
}
