using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using FusionNet.Core.Models;
using FusionNet.Core.Planning;
using FusionNet.Core.Utilities;
using FusionNet.Tests.Support;

namespace FusionNet.Tests;

/// <summary>
/// Regression coverage for the defects confirmed during the pre-delivery hardening audit of
/// <c>FusionNet.Core</c>. Every test here failed against the original implementation (or pins a
/// safety property that was silently missing) and must keep passing once the fix is in place.
/// </summary>
public class HardeningRegressionTests
{
    private const string PingTarget = "1.1.1.1";

    // ============================================================ FusionJson: atomic saves

    /// <summary>
    /// Fix: <see cref="FusionJson.SaveAtomic{T}"/> used one fixed "&lt;file&gt;.tmp" name, so two
    /// writers saving the same document raced on the same temp file: one writer could truncate the
    /// other's temp while it was being moved, publishing a half-written JSON file (or throwing).
    /// A unique temp file per write plus a short retry on the atomic replace fixes both.
    /// </summary>
    [Fact]
    public async Task SaveAtomic_ConcurrentWriters_NeverCorruptTheFileOrLeakTempFiles()
    {
        using var temp = new TempDirectory();
        var directory = temp.Combine("concurrent");
        var path = Path.Combine(directory, "journal.json");
        var failures = new ConcurrentBag<Exception>();
        var sessionIds = new ConcurrentBag<string>();

        const int writers = 6;
        const int rounds = 3;
        for (var round = 0; round < rounds; round++)
        {
            using var start = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
            {
                var document = new JournalDocument { Active = true, GameName = "Concurrent" };
                for (var i = 0; i < 150; i++)
                {
                    document.Entries.Add(JournalEntry.Create(
                        JournalKind.Service, "wuauserv", "pause " + i, "running", "stopped"));
                }

                sessionIds.Add(document.SessionId);
                start.Wait();
                try
                {
                    FusionJson.SaveAtomic(path, document);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            })).ToArray();

            start.Set();
            await Task.WhenAll(tasks);
        }

        Assert.Empty(failures);
        Assert.True(File.Exists(path), "The journal file must exist after concurrent saves.");
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

        var loaded = FusionJson.LoadOrCreate(path, () => new JournalDocument { GameName = "missing" });
        Assert.Equal("Concurrent", loaded.GameName);
        Assert.Equal(150, loaded.Entries.Count);
        Assert.Contains(loaded.SessionId, sessionIds);
    }

    [Fact]
    public void LoadOrCreate_ReadsAFileWithUtf8ByteOrderMark()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("settings-bom.json");
        File.WriteAllText(
            path,
            FusionJson.Serialize(new FusionSettings { Accent = "rose", PingIntervalMs = 800 }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var loaded = FusionJson.LoadOrCreate(path, FusionSettings.CreateDefault);

        Assert.Equal("rose", loaded.Accent);
        Assert.Equal(800, loaded.PingIntervalMs);
    }

    // ==================================================== FusionSettings: self-healing contract

    /// <summary>
    /// Fix: <see cref="FusionSettings.Normalize"/> called <see cref="Path.GetExtension"/> and
    /// <see cref="Path.GetFullPath"/> on persisted custom-app/custom-game paths. A corrupted
    /// settings file containing a NUL or otherwise illegal path threw
    /// <see cref="ArgumentException"/> out of <c>Normalize</c>, crashing the app on startup instead
    /// of dropping the bad entry. Trailing spaces (common when pasting a path) also hid a valid .exe.
    /// </summary>
    [Fact]
    public void Normalize_DropsIllegalPathsAndTrimsPastedExecutablePaths()
    {
        var nul = '\0';
        var settings = new FusionSettings
        {
            CustomApps =
            [
                new CustomManagedApp("bad-nul", "Bad", "C:\\Games\\bad" + nul + "app.exe", AppControlAction.Suspend),
                new CustomManagedApp("spaced", "Spaced", @"C:\Games\tool.exe   ", AppControlAction.Suspend),
                new CustomManagedApp("good", "Good", @"C:\Games\Good.exe", AppControlAction.Suspend)
            ],
            CustomGames =
            [
                new CustomGame("bad-game", "Bad", "C:\\Games\\bad" + nul + "game.exe"),
                new CustomGame("spaced-game", "Spaced", @"C:\Games\SpacedGame.exe  "),
                new CustomGame("good-game", "Good", @"C:\Games\GoodGame.exe")
            ]
        };

        var normalized = settings.Normalize();

        Assert.Equal(2, normalized.CustomApps.Count);
        Assert.Contains(normalized.CustomApps, app => app.Id == "spaced" &&
            string.Equals(app.ExecutablePath, Path.GetFullPath(@"C:\Games\tool.exe"), StringComparison.Ordinal));
        Assert.Contains(normalized.CustomApps, app => app.Id == "good");

        Assert.Equal(2, normalized.CustomGames.Count);
        Assert.Contains(normalized.CustomGames, game => game.Id == "spaced-game" &&
            string.Equals(game.ExecutablePath, Path.GetFullPath(@"C:\Games\SpacedGame.exe"), StringComparison.Ordinal));
        Assert.Contains(normalized.CustomGames, game => game.Id == "good-game");
    }

    /// <summary>
    /// Fix: a known accent id was accepted in any casing (<c>"VIOLET"</c>) or rejected when padded
    /// with spaces, so the persisted value did not match the canonical ids the theme engine uses.
    /// </summary>
    [Fact]
    public void Normalize_CanonicalisesKnownAccentIds()
    {
        var settings = new FusionSettings { Accent = "VIOLET" };
        settings.Normalize();
        Assert.Equal("violet", settings.Accent);

        settings.Accent = "  Amber  ";
        settings.Normalize();
        Assert.Equal("amber", settings.Accent);

        settings.Accent = "purple";
        settings.Normalize();
        Assert.Equal(AccentCatalog.DefaultId, settings.Accent);
    }

    /// <summary>
    /// Fix: Normalize only checked that the hotkey parsed, not that it could ever be registered.
    /// Named keys such as Space parse but map to virtual-key 0, so a persisted "Ctrl+Alt+Space"
    /// silently disabled the global hotkey. Also stores the canonical "Ctrl+Alt+F" form.
    /// </summary>
    [Fact]
    public void Normalize_CanonicalisesHotkeysAndRejectsUnregistrableOnes()
    {
        var settings = new FusionSettings { Hotkey = "ctrl+alt+f" };
        settings.Normalize();
        Assert.Equal("Ctrl+Alt+F", settings.Hotkey);

        settings.Hotkey = "Ctrl+Alt+Space"; // parses, but ToVirtualKey() is 0
        settings.Normalize();
        Assert.Equal(HotkeyGesture.Default.ToString(), settings.Hotkey);

        settings.Hotkey = "Shift+PageUp";
        settings.Normalize();
        Assert.Equal(HotkeyGesture.Default.ToString(), settings.Hotkey);
    }

    [Fact]
    public void Normalize_ResetsPinnedGameWhenTheCustomGameIsDisabled()
    {
        var settings = new FusionSettings
        {
            SelectedGameId = "custom-off",
            CustomGames = [new CustomGame("custom-off", "Off", @"C:\Games\Off.exe", Enabled: false)]
        };

        settings.Normalize();

        Assert.Equal(GameCatalog.AutoProfileId, settings.SelectedGameId);
    }

    [Fact]
    public void Normalize_CanonicalisesSelectedBuiltInGameId()
    {
        var settings = new FusionSettings { SelectedGameId = "VALORANT" };
        settings.Normalize();
        Assert.Equal("valorant", settings.SelectedGameId);
    }

    [Fact]
    public void Normalize_GeneratesIdentityForCustomGamesWithBlankId()
    {
        var settings = new FusionSettings
        {
            CustomGames = [new CustomGame("   ", "Game", @"C:\Games\Game.exe")]
        };

        settings.Normalize();

        var game = Assert.Single(settings.CustomGames);
        Assert.StartsWith("custom-", game.Id);
        Assert.Equal(CustomGame.Create(game.ExecutablePath).Id, game.Id);
    }

    [Fact]
    public void Normalize_TrimsAndKeepsValidCustomDnsServers()
    {
        var settings = new FusionSettings
        {
            CustomDnsServers = [" 1.1.1.1 ", "8.8.8.8", " 1.1.1.1", "1.1", "010.1.1.1"]
        };

        settings.Normalize();

        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8" }, settings.CustomDnsServers);
    }

    [Fact]
    public void GetAction_NullIdIsTreatedAsUnknown()
    {
        var settings = FusionSettings.CreateDefault();

        Assert.Equal(AppControlAction.Ignore, settings.GetAction(null!));
        Assert.Equal(AppControlAction.Ignore, settings.GetAction("  "));
    }

    /// <summary>
    /// Fix: <see cref="FusionSettings.Clone"/> round-tripped through JSON, which re-created
    /// <c>AppActions</c> with the default case-sensitive comparer. The clone lost case-insensitive
    /// app lookups and could grow duplicate keys when edited.
    /// </summary>
    [Fact]
    public void Clone_PreservesCaseInsensitiveAppActionLookup()
    {
        var settings = FusionSettings.CreateDefault();
        settings.SetAction("steam", AppControlAction.Suspend);

        var clone = settings.Clone();

        Assert.Equal(AppControlAction.Suspend, clone.GetAction("STEAM"));
        Assert.Equal(AppControlAction.Suspend, clone.GetAction("steam"));

        clone.SetAction("STEAM", AppControlAction.Throttle);
        Assert.Equal(AppControlAction.Throttle, clone.GetAction("steam"));
        Assert.Single(clone.AppActions.Keys,
            key => string.Equals(key, "steam", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AppControlAction.Suspend, settings.GetAction("steam"));
    }

    // ================================================================ Game catalog defects

    /// <summary>
    /// Fix: an unknown pinned game id made <see cref="GameCatalog.BuildWatchList"/> return only the
    /// custom games, silently disabling built-in game detection until the setting was repaired.
    /// Unknown pins must fall back to the automatic watch list.
    /// </summary>
    [Fact]
    public void BuildWatchList_UnknownPinnedGameFallsBackToAutoWatchList()
    {
        var settings = new FusionSettings
        {
            SelectedGameId = "totally-not-a-game",
            CustomGames = [new CustomGame("custom-on", "On", @"C:\Games\On.exe")]
        };

        var list = GameCatalog.BuildWatchList(settings);

        Assert.Equal(GameCatalog.All.Count + 1, list.Count);
        Assert.Contains(list, game => game.Id == "counter-strike-2");
        Assert.Contains(list, game => game.Id == "custom-on");
    }

    /// <summary>Fix: <see cref="CustomGame.Create"/> accepted any file, unlike the managed-app path,
    /// so a text file could be added and then silently disappear during settings normalization.</summary>
    [Fact]
    public void CustomGame_Create_RejectsNonExecutableFiles()
    {
        Assert.Throws<ArgumentException>(() => CustomGame.Create(@"C:\Games\notes.txt"));
        Assert.Contains("Game.exe", CustomGame.Create(@"C:\Games\Game.exe").ExecutablePath);
    }

    /// <summary>Fix: Fortnite and Apex Legends shared the Steam app id 1172470, which the Gaming
    /// page displays as the game's store id. A Steam app id must identify at most one game.</summary>
    [Fact]
    public void GameCatalog_SteamAppIdsAreUniqueAndFortniteHasNone()
    {
        var ids = GameCatalog.All
            .Where(game => game.SteamAppId is not null)
            .Select(game => game.SteamAppId!)
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Null(GameCatalog.Find("fortnite")!.SteamAppId);
        Assert.Equal("1172470", GameCatalog.Find("apex-legends")!.SteamAppId);
    }

    // ============================================================== Hotkey parsing defects

    /// <summary>
    /// Fix: <see cref="HotkeyGesture.TryParse"/> used <see cref="char.IsLetterOrDigit(char)"/>,
    /// which accepts non-ASCII letters and Arabic-Indic digits; those keys can never map to a Win32
    /// virtual key, so the gesture parsed but could not be registered.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+ف")]
    [InlineData("Ctrl+é")]
    [InlineData("Ctrl+٣")]
    [InlineData("Alt+Ж")]
    public void TryParse_RejectsNonAsciiKeysThatCannotBeRegistered(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    // ============================================================== DNS validation defects

    /// <summary>
    /// Fix: <see cref="DnsCatalog.IsValidIPv4"/> delegated to <see cref="System.Net.IPAddress.TryParse(string, out System.Net.IPAddress)"/>,
    /// which also accepts legacy shorthand ("1.1" = 1.0.0.1) and octal/hex octets ("010.1.1.1" = 8.1.1.1).
    /// Those strings were stored as custom DNS servers and then configured a different server than
    /// the user typed (or failed at the netsh layer).
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("1.1")]
    [InlineData("127.1")]
    [InlineData("01.1.1.1")]
    [InlineData("010.1.1.1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("1.1.1.1/24")]
    [InlineData("1.1.1.1 ")]
    public void IsValidIPv4_RejectsShorthandOctalAndCidrForms(string value)
    {
        Assert.False(DnsCatalog.IsValidIPv4(value));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("192.168.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void IsValidIPv4_AcceptsCanonicalDottedQuad(string value)
    {
        Assert.True(DnsCatalog.IsValidIPv4(value));
    }

    // ================================================================= Humanize defects

    /// <summary>
    /// Fix: <see cref="Humanize.Bytes(double)"/>, <see cref="Humanize.Speed"/> and
    /// <see cref="DnsBenchmarkResult.LatencyDisplay"/> interpolated numbers with the current culture,
    /// while <c>Ms</c>/<c>Percent</c> were invariant. On a comma-decimal locale the same metric row
    /// would mix "12.3 ms" with "1,5 KB"/"1,5 Mbps". Metrics must stay invariant (Latin digits, "."
    /// separator) and only user-facing text is localized.
    /// </summary>
    [Fact]
    public void DisplayMetrics_UseInvariantCultureRegardlessOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal separator ","

            Assert.Equal("1.5 KB", Humanize.Bytes(1536));
            Assert.Equal("1.5 KB/s", Humanize.Rate(1536));
            Assert.Equal("1.5 Mbps", Humanize.Speed(1_500_000));
            Assert.Equal("12.3 ms", Humanize.Ms(12.34, 1));
            Assert.Equal("12.3%", Humanize.Percent(12.34, 1));
            Assert.Equal("12.3 ms",
                new DnsBenchmarkResult("cloudflare", "Cloudflare", "1.1.1.1", 12.34, true).LatencyDisplay);

            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal("1.5 GB", Humanize.Bytes(1_610_612_736));
            Assert.Equal("1.5 Mbps", Humanize.Speed(1_500_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NonFiniteMetrics_RenderAsUnknownInsteadOfGarbage()
    {
        Assert.Equal("—", Humanize.Ms(double.NaN));
        Assert.Equal("—", Humanize.Ms(double.PositiveInfinity));
        Assert.Equal("—", Humanize.Percent(double.NaN));
        Assert.Equal("—", Humanize.Percent(double.NegativeInfinity));
        Assert.Equal("0 B", Humanize.Bytes(double.NaN));
        Assert.Equal("0 B", Humanize.Bytes(double.NegativeInfinity));
    }

    [Fact]
    public void Bytes_ExtremeFiniteValuesStayWithinKnownUnits()
    {
        var bytes = Humanize.Bytes(double.MaxValue);
        Assert.EndsWith(" PB", bytes);
        Assert.DoesNotContain("E+", bytes);
        Assert.Equal("10000 PB", Humanize.Bytes(10_000d * 1024 * 1024 * 1024 * 1024 * 1024));
    }

    [Fact]
    public void KnownPaths_ExpandsBothProgramFilesAliases()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        Assert.Equal(Path.Combine(programFiles, "App", "a.exe"), KnownPaths.Expand(@"%ProgramFiles%\App\a.exe"));
        Assert.Equal(Path.Combine(programFilesX86, "App", "a.exe"), KnownPaths.Expand(@"%ProgramFiles(x86)%\App\a.exe"));
        Assert.Null(KnownPaths.Expand(@"%NOT_A_REAL_ENV_VAR%\App\a.exe"));
    }

    // ================================================== Ping statistics correctness defects

    /// <summary>
    /// Fix: a non-finite round-trip time (infinity) was counted as a received sample, which made
    /// min/max/average and therefore the quality score non-finite (rendered later as "NaN ms").
    /// Only finite, non-negative RTTs are real measurements.
    /// </summary>
    [Fact]
    public void Add_NonFiniteRoundTripsAreCountedAsLost()
    {
        var accumulator = new PingStatisticsAccumulator(PingTarget);
        accumulator.Add(new PingSample(PingTarget, 1, 10, DateTimeOffset.UtcNow));
        accumulator.Add(new PingSample(PingTarget, 2, double.PositiveInfinity, DateTimeOffset.UtcNow));
        accumulator.Add(new PingSample(PingTarget, 3, double.NaN, DateTimeOffset.UtcNow));

        var stats = accumulator.Snapshot();

        Assert.Equal(3, stats.Sent);
        Assert.Equal(1, stats.Received);
        Assert.Equal(2, stats.Lost);
        Assert.True(double.IsFinite(stats.AverageMs));
        Assert.True(double.IsFinite(stats.JitterMs));
        Assert.True(double.IsFinite(stats.MaxMs));
        Assert.InRange(stats.QualityScore, 0, 100);
    }

    /// <summary>
    /// Fix: the rolling average summed every RTT in a <see cref="double"/>; two extreme (but finite)
    /// samples overflowed to +Infinity. The average/jitter are now accumulated incrementally.
    /// </summary>
    [Fact]
    public void Add_ExtremeFiniteValuesDoNotOverflowTheAverage()
    {
        var accumulator = new PingStatisticsAccumulator(PingTarget);
        accumulator.Add(new PingSample(PingTarget, 1, double.MaxValue, DateTimeOffset.UtcNow));
        accumulator.Add(new PingSample(PingTarget, 2, double.MaxValue, DateTimeOffset.UtcNow));

        var stats = accumulator.Snapshot();

        Assert.Equal(2, stats.Received);
        Assert.True(double.IsFinite(stats.AverageMs));
        Assert.True(stats.AverageMs > 1e300);
        Assert.True(double.IsFinite(stats.JitterMs));
    }

    [Fact]
    public async Task Add_IsSafeUnderConcurrentProbes()
    {
        var accumulator = new PingStatisticsAccumulator(PingTarget, capacity: 500);
        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                accumulator.Add(new PingSample(PingTarget, worker * 1000 + i, 10 + (i % 5), DateTimeOffset.UtcNow));
            }
        })).ToArray();

        await Task.WhenAll(workers);
        var stats = accumulator.Snapshot();

        Assert.Equal(2000, stats.Sent);
        Assert.Equal(2000, stats.Received);
        Assert.Equal(0, stats.Lost);
        Assert.InRange(stats.AverageMs, 10, 14);
        Assert.Equal(2000, stats.Sent); // Snapshot keeps the cumulative counters stable
    }

    /// <summary>
    /// Fix: <see cref="PingSample.Success"/> only checked <c>RoundTripMs.HasValue</c>, so a negative,
    /// NaN or infinite value counted as a success even though the statistics layer treated it as lost.
    /// </summary>
    [Fact]
    public void PingSample_SuccessRequiresAFiniteNonNegativeRoundTrip()
    {
        Assert.False(new PingSample(PingTarget, 1, -5, DateTimeOffset.UtcNow).Success);
        Assert.False(new PingSample(PingTarget, 2, double.NaN, DateTimeOffset.UtcNow).Success);
        Assert.False(new PingSample(PingTarget, 3, double.PositiveInfinity, DateTimeOffset.UtcNow).Success);
        Assert.False(new PingSample(PingTarget, 4, null, DateTimeOffset.UtcNow, "timeout").Success);
        Assert.True(new PingSample(PingTarget, 5, 0, DateTimeOffset.UtcNow).Success);
        Assert.True(new PingSample(PingTarget, 6, 12.5, DateTimeOffset.UtcNow).Success);
    }

    // ============================================================= Catalog immutability

    /// <summary>
    /// Fix: the static catalogs were typed as <see cref="IReadOnlyList{T}"/> but returned the backing
    /// arrays, so any caller could cast back to <c>IList&lt;T&gt;</c> and mutate global catalog state
    /// (breaking detection, planning and the UI for the rest of the process).
    /// </summary>
    [Fact]
    public void StaticCatalogs_ExposeReadOnlyCollections()
    {
        AssertReadOnly(GameCatalog.All);
        AssertReadOnly(GameCatalog.Categories);
        AssertReadOnly(ManagedAppCatalog.All);
        AssertReadOnly(ManagedAppCatalog.Categories);
        AssertReadOnly(TweakCatalog.All);
        AssertReadOnly(TweakCatalog.Recommended);
        AssertReadOnly(TweakCatalog.RecommendedIds);
        AssertReadOnly(ServiceCatalog.All);
        AssertReadOnly(ServiceCatalog.DefaultPausedIds);
        AssertReadOnly(DnsCatalog.All);
        AssertReadOnly(DnsCatalog.Recommended);
        AssertReadOnly(AccentCatalog.All);

        // Nested per-item collections must not be mutable through the interface either.
        AssertReadOnly(GameCatalog.All[0].ProcessNames);
        AssertReadOnly(ManagedAppCatalog.All[0].ProcessNames);
        AssertReadOnly(DnsCatalog.All[0].Servers);
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> list)
    {
        Assert.NotEmpty(list);
        Assert.Throws<NotSupportedException>(() => { ((IList<T>)list)[0] = list[0]; });
    }

    // ================================================================= Planner robustness

    /// <summary>
    /// Fix: <see cref="OptimizationPlanner.Build"/> dereferenced every element of
    /// <c>settings.CustomApps</c>; a JSON file containing a <c>null</c> array element made the
    /// planner throw before the settings could be repaired by <c>Normalize</c>.
    /// </summary>
    [Fact]
    public void Build_ToleratesNullCustomAppEntries()
    {
        var settings = FusionSettings.CreateDefault();
        settings.AppActions.Clear();
        settings.CustomApps.Add(null!);

        var plan = OptimizationPlanner.Build(settings, null, []);

        Assert.Empty(plan.AppRules);
    }

    // ============================================================ JSON culture independence

    [Fact]
    public void JsonRoundTrip_IsCultureIndependent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "de-DE", "ar-SA", "fa-IR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                var entry = JournalEntry.Create(JournalKind.Dns, "adapter", "dns changed", "dhcp", "1.1.1.1,1.0.0.1");
                entry.AppliedAtUtc = new DateTimeOffset(2026, 3, 1, 12, 34, 56, 789, TimeSpan.Zero);

                var json = FusionJson.Serialize(entry);
                Assert.Contains("\"kind\": \"dns\"", json);
                Assert.Contains("2026-03-01T12:34:56.789", json);

                var loaded = FusionJson.Deserialize<JournalEntry>(json);
                Assert.NotNull(loaded);
                Assert.Equal(entry.AppliedAtUtc, loaded!.AppliedAtUtc);
                Assert.Equal(entry.Id, loaded.Id);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ================================================================ JSON option contract

    [Fact]
    public void JsonOptions_IgnoreUnknownMembersAndAcceptIntegerEnums()
    {
        // Forward compatibility: a settings file written by a newer build must still load.
        var settings = FusionJson.Deserialize<FusionSettings>("""
            {
              "language": "en",
              "accent": "rose",
              "futureFeature": { "nested": true },
              "pingIntervalMs": 750
            }
            """);

        Assert.NotNull(settings);
        Assert.Equal("rose", settings!.Accent);
        Assert.Equal(750, settings.PingIntervalMs);

        // Old builds persisted enums numerically; those files must keep loading too.
        var entry = FusionJson.Deserialize<JournalEntry>("""
            { "kind": 4, "target": "rule", "description": "block" }
            """);

        Assert.NotNull(entry);
        Assert.Equal(JournalKind.Firewall, entry!.Kind);
    }

    [Fact]
    public void JsonRoundTrip_PreservesAppControlActions()
    {
        var settings = FusionSettings.CreateDefault();
        settings.SetAction("qbittorrent", AppControlAction.BlockNetwork);
        settings.SetAction("steam", AppControlAction.Throttle);

        var json = FusionJson.Serialize(settings);
        Assert.Contains("\"blockNetwork\"", json);

        var loaded = FusionJson.Deserialize<FusionSettings>(json);
        Assert.NotNull(loaded);
        Assert.Equal(AppControlAction.BlockNetwork, loaded!.GetAction("qbittorrent"));
        Assert.Equal(AppControlAction.Throttle, loaded.GetAction("steam"));
    }

    /// <summary>
    /// Fix: firewall rule ordering only sorted by display name; two rules for the same application
    /// (x86 and x64 install locations share a display name) came out in dictionary/enumeration order,
    /// so the same machine state could produce a different rule order between plan refreshes.
    /// </summary>
    [Fact]
    public void FirewallPlanner_RulesAreDeterministicallyOrderedForEqualDisplayNames()
    {
        static AppRuleTarget Target(string path) =>
            new("same-app", "Same App", AppControlAction.BlockNetwork, ["same"], [], path);

        var forward = FirewallPlanner.Build(
            [Target(@"C:\Apps\first.exe"), Target(@"C:\Apps\second.exe")], [], null);
        var reversed = FirewallPlanner.Build(
            [Target(@"C:\Apps\second.exe"), Target(@"C:\Apps\first.exe")], [], null);

        Assert.Equal(2, forward.Count);
        Assert.Equal(2, reversed.Count);
        Assert.Equal(forward.Select(rule => rule.Name), reversed.Select(rule => rule.Name));
        Assert.Equal(
            forward.Select(rule => rule.Name).OrderBy(name => name, StringComparer.Ordinal),
            forward.Select(rule => rule.Name));
    }

    /// <summary>Fix: <see cref="FirewallPlanner.BuildServiceRules"/> emitted one rule per input
    /// entry, so a duplicated service definition produced duplicate rule names.</summary>
    [Fact]
    public void FirewallPlanner_BuildServiceRulesDedupesAndOrdersRules()
    {
        using var temp = new TempDirectory();
        var svchost = temp.CreateFile(@"System32\svchost.exe");
        var services = new[] { ServiceCatalog.All[0], ServiceCatalog.All[1], ServiceCatalog.All[0] };

        var rules = FirewallPlanner.BuildServiceRules(services, svchost);

        Assert.Equal(2, rules.Count);
        Assert.Equal(rules.Count, rules.Select(rule => rule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            rules.Select(rule => rule.Name).OrderBy(name => name, StringComparer.Ordinal),
            rules.Select(rule => rule.Name));
    }

    // ================================================================ Test infrastructure

    [Fact]
    public void TempDirectory_ProvidesIsolatedWritableRootsAndCleansUp()
    {
        var first = new TempDirectory();
        var second = new TempDirectory();
        var firstRoot = first.Root;
        var secondRoot = second.Root;

        try
        {
            Assert.NotEqual(firstRoot, secondRoot);
            Assert.True(Directory.Exists(firstRoot));
            Assert.True(Directory.Exists(secondRoot));

            var file = first.CreateFile(Path.Combine("nested", "data.json"), "{}");
            Assert.Equal(Path.Combine(firstRoot, "nested", "data.json"), file);
            Assert.True(File.Exists(file));
            Assert.False(File.Exists(Path.Combine(secondRoot, "nested", "data.json")));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }

        Assert.False(Directory.Exists(firstRoot));
        Assert.False(Directory.Exists(secondRoot));
    }
}
