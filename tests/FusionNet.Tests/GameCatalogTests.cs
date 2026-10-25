using FusionNet.Core.Models;

namespace FusionNet.Tests;

/// <summary>Game catalogue, watch-list construction and running-game detection precedence.</summary>
public class GameCatalogTests
{
    [Fact]
    public void Catalog_ContainsExpectedGamesWithUniqueIds()
    {
        Assert.Equal(22, GameCatalog.All.Count);
        Assert.Equal(GameCatalog.All.Count,
            GameCatalog.All.Select(game => game.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.All(GameCatalog.All, game =>
        {
            Assert.False(string.IsNullOrWhiteSpace(game.Id));
            Assert.False(string.IsNullOrWhiteSpace(game.Name));
            Assert.False(string.IsNullOrWhiteSpace(game.Category));
            Assert.NotEmpty(game.ProcessNames);
        });

        Assert.Contains("Tactical FPS", GameCatalog.Categories);
        Assert.Contains("Battle Royale", GameCatalog.Categories);
        Assert.DoesNotContain(GameCatalog.AutoProfileId, GameCatalog.All.Select(game => game.Id));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var valorant = GameCatalog.Find("VALORANT");
        Assert.NotNull(valorant);
        Assert.Equal("valorant", valorant!.Id);
        Assert.Equal("VALORANT", valorant.Name);

        Assert.Null(GameCatalog.Find("no-such-game"));
        Assert.Null(GameCatalog.Find(null));
        Assert.Null(GameCatalog.Find("  "));
    }

    [Fact]
    public void MatchesProcess_IsCaseInsensitiveAndExact()
    {
        var cs2 = GameCatalog.Find("counter-strike-2")!;
        Assert.True(cs2.MatchesProcess("CS2"));
        Assert.True(cs2.MatchesProcess("csgo"));
        Assert.False(cs2.MatchesProcess("cs2 "));
        Assert.False(cs2.MatchesProcess("counter-strike-2"));
        Assert.True(cs2.MatchesAnyProcess(["notepad", "cs2"]));
        Assert.False(cs2.MatchesAnyProcess(["notepad", "chrome"]));
    }

    [Fact]
    public void BuildWatchList_Auto_IncludesAllBuiltInsAndEnabledCustomGames()
    {
        var settings = new FusionSettings
        {
            SelectedGameId = GameCatalog.AutoProfileId,
            CustomGames =
            [
                new CustomGame("custom-on", "On", @"C:\Games\On.exe"),
                new CustomGame("custom-off", "Off", @"C:\Games\Off.exe", Enabled: false)
            ]
        };

        var list = GameCatalog.BuildWatchList(settings);

        Assert.Equal(GameCatalog.All.Count + 1, list.Count);
        Assert.Contains(list, game => game.Id == "custom-on");
        Assert.DoesNotContain(list, game => game.Id == "custom-off");
        Assert.Equal("Custom", list[^1].Category);
    }

    [Fact]
    public void BuildWatchList_PinnedBuiltIn_StartsWithPinnedGameButKeepsCustomGames()
    {
        var settings = new FusionSettings
        {
            SelectedGameId = "valorant",
            CustomGames = [new CustomGame("custom-x", "X", @"C:\Games\X.exe")]
        };

        var list = GameCatalog.BuildWatchList(settings);

        Assert.Equal(2, list.Count);
        Assert.Equal("valorant", list[0].Id);
        Assert.Equal("custom-x", list[1].Id);
    }

    [Fact]
    public void BuildWatchList_PinnedCustom_KeepsAllCustomGames()
    {
        var settings = new FusionSettings
        {
            SelectedGameId = "custom-a",
            CustomGames =
            [
                new CustomGame("custom-a", "A", @"C:\Games\A.exe"),
                new CustomGame("custom-b", "B", @"C:\Games\B.exe")
            ]
        };

        var list = GameCatalog.BuildWatchList(settings);

        Assert.Equal(2, list.Count);
        Assert.All(list, game => Assert.Equal("Custom", game.Category));
        Assert.Contains(list, game => game.Id == "custom-a");
        Assert.Contains(list, game => game.Id == "custom-b");
    }

    [Fact]
    public void BuildWatchList_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GameCatalog.BuildWatchList(null!));
    }

    [Fact]
    public void DetectRunning_NoGames_ReturnsNull()
    {
        var watchList = GameCatalog.BuildWatchList(FusionSettings.CreateDefault());

        Assert.Null(GameCatalog.DetectRunning(watchList, ["notepad", "chrome"], null));
        Assert.Null(GameCatalog.DetectRunning(watchList, Array.Empty<string>(), null));
    }

    [Fact]
    public void DetectRunning_SingleMatch_ReturnsIt()
    {
        var watchList = GameCatalog.BuildWatchList(FusionSettings.CreateDefault());

        var detected = GameCatalog.DetectRunning(watchList, ["notepad", "cs2"], null);

        Assert.NotNull(detected);
        Assert.Equal("counter-strike-2", detected!.Id);
    }

    [Fact]
    public void DetectRunning_MultipleMatches_PinnedGameWins()
    {
        var watchList = GameCatalog.BuildWatchList(FusionSettings.CreateDefault());
        string[] running = ["cs2", "VALORANT-Win64-Shipping"];

        var pinned = GameCatalog.DetectRunning(watchList, running, "valorant");
        Assert.NotNull(pinned);
        Assert.Equal("valorant", pinned!.Id);

        // A pin that is not among the running games must not win.
        var unmatchedPin = GameCatalog.DetectRunning(watchList, running, "apex-legends");
        Assert.NotNull(unmatchedPin);
        Assert.Equal("valorant", unmatchedPin!.Id); // first match in watch-list order
    }

    [Fact]
    public void DetectRunning_MultipleMatches_FirstMatchWhenNoPin()
    {
        var watchList = GameCatalog.BuildWatchList(FusionSettings.CreateDefault());

        var detected = GameCatalog.DetectRunning(watchList, ["cs2", "VALORANT-Win64-Shipping"], null);

        Assert.NotNull(detected);
        Assert.Equal("valorant", detected!.Id);
    }

    [Fact]
    public void DetectRunning_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => GameCatalog.DetectRunning(null!, Array.Empty<string>(), null));
        Assert.Throws<ArgumentNullException>(() => GameCatalog.DetectRunning([], null!, null));
    }

    [Fact]
    public void CustomGame_Create_BuildsStableIdentity()
    {
        var path = @"C:\Games\MyGame.exe";

        var game = CustomGame.Create(path);

        Assert.StartsWith("custom-", game.Id);
        Assert.Equal(12, game.Id["custom-".Length..].Length);
        Assert.Equal("MyGame", game.Name);
        Assert.Equal("MyGame", game.ProcessName);
        Assert.Equal(Path.GetFullPath(path), game.ExecutablePath);
        Assert.True(game.Enabled);

        var samePathDifferentCase = CustomGame.Create(path.ToLowerInvariant());
        Assert.Equal(game.Id, samePathDifferentCase.Id);
    }

    [Fact]
    public void CustomGame_Create_RejectsMissingFileName()
    {
        Assert.Throws<ArgumentException>(() => CustomGame.Create(@"C:\"));
        Assert.ThrowsAny<ArgumentException>(() => CustomGame.Create(""));
    }

    [Fact]
    public void CustomGame_ToProfile_IsDetectable()
    {
        var game = new CustomGame("custom-hero", "Hero", @"C:\Games\Hero.exe");

        var profile = game.ToProfile();

        Assert.Equal("custom-hero", profile.Id);
        Assert.Equal("Custom", profile.Category);
        Assert.True(profile.MatchesProcess("hero"));
        Assert.True(profile.MatchesProcess("HERO"));
        Assert.False(profile.MatchesProcess("hero2"));
    }
}
