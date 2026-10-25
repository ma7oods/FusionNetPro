using FusionNet.Core.Models;
using FusionNet.Core.Utilities;
using FusionNet.Tests.Support;

namespace FusionNet.Tests;

/// <summary>
/// Persistence contract: camelCase JSON, enums as strings, atomic saves and graceful recovery
/// from corrupted files. A half-written settings or journal file must never crash the app.
/// </summary>
public class FusionJsonTests
{
    [Fact]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var json = FusionJson.Serialize(FusionSettings.CreateDefault());

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"language\"", json);
        Assert.Contains("\"appActions\"", json);
        Assert.Contains("\"pingTargets\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
        Assert.DoesNotContain("\"Language\"", json);
    }

    [Fact]
    public void Serialize_WritesEnumsAsCamelCaseStrings()
    {
        var entry = JournalEntry.Create(JournalKind.Registry, "tweak:network-throttling", "apply tweak");

        var json = FusionJson.Serialize(entry);

        Assert.Contains("\"kind\": \"registry\"", json);
        Assert.DoesNotContain("\"kind\": 1", json);
    }

    [Fact]
    public void Deserialize_ReadsEnumsFromStrings()
    {
        var entry = FusionJson.Deserialize<JournalEntry>("""
            { "kind": "powerPlan", "target": "powerplan", "description": "high performance" }
            """);

        Assert.NotNull(entry);
        Assert.Equal(JournalKind.PowerPlan, entry!.Kind);
        Assert.Equal("powerplan", entry.Target);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitiveAndTolerant()
    {
        var settings = FusionJson.Deserialize<FusionSettings>("""
            {
              // a comment left by a future version
              "Language": "en",
              "Accent": "rose",
              "PingIntervalMs": 800,
            }
            """);

        Assert.NotNull(settings);
        Assert.Equal("en", settings!.Language);
        Assert.Equal("rose", settings.Accent);
        Assert.Equal(800, settings.PingIntervalMs);
    }

    [Fact]
    public void SaveAtomic_LoadOrCreate_RoundTrips()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("data", "journal.json");

        var document = new JournalDocument
        {
            Active = true,
            GameName = "Counter-Strike 2",
            Entries =
            [
                JournalEntry.Create(JournalKind.Service, "wuauserv", "pause Windows Update", "running", "stopped"),
                JournalEntry.Create(JournalKind.Firewall, "FNP_APP_1", "block qBittorrent", "absent", "qBittorrent")
            ]
        };

        FusionJson.SaveAtomic(path, document);
        var loaded = FusionJson.LoadOrCreate(path, () => new JournalDocument());

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.NotNull(loaded);
        Assert.Equal(document.SessionId, loaded.SessionId);
        Assert.True(loaded.Active);
        Assert.Equal("Counter-Strike 2", loaded.GameName);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(JournalKind.Firewall, loaded.Entries[1].Kind);
        Assert.Equal("absent", loaded.Entries[1].PreviousValue);
    }

    [Fact]
    public void SaveAtomic_CreatesMissingDirectories()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("nested", "deeper", "settings.json");

        FusionJson.SaveAtomic(path, FusionSettings.CreateDefault());

        Assert.True(File.Exists(path));
        Assert.NotEmpty(File.ReadAllText(path));
    }

    [Fact]
    public void SaveAtomic_OverwritesExistingFile()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("settings.json", "{\"language\":\"en\"}");

        FusionJson.SaveAtomic(path, new FusionSettings { Language = "ar", Accent = "violet" });
        var loaded = FusionJson.LoadOrCreate(path, FusionSettings.CreateDefault);

        Assert.Equal("ar", loaded.Language);
        Assert.Equal("violet", loaded.Accent);
    }

    [Fact]
    public void LoadOrCreate_MissingFile_ReturnsFallback()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("does-not-exist.json");

        var loaded = FusionJson.LoadOrCreate(path, FusionSettings.CreateDefault);

        Assert.Equal("en", loaded.Language);
        Assert.Equal(AccentCatalog.DefaultId, loaded.Accent);
    }

    [Fact]
    public void LoadOrCreate_CorruptFile_ReturnsFallback()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("settings.json", "{ this is definitely not json ");

        var loaded = FusionJson.LoadOrCreate(path, () => new FusionSettings { Language = "en", Accent = "rose" });

        Assert.Equal("en", loaded.Language);
        Assert.Equal("rose", loaded.Accent);
    }

    [Fact]
    public void LoadOrCreate_JsonNull_ReturnsFallback()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("journal.json", "null");

        var loaded = FusionJson.LoadOrCreate(path, () => new JournalDocument { GameName = "fallback" });

        Assert.Equal("fallback", loaded.GameName);
    }

    [Fact]
    public void RoundTrip_PreservesJournalEntryDefaults()
    {
        var entry = JournalEntry.Create(JournalKind.Memory, "standby", "purge standby list", "-", "1048576");

        var loaded = FusionJson.Deserialize<JournalEntry>(FusionJson.Serialize(entry));

        Assert.NotNull(loaded);
        Assert.Equal(32, loaded!.Id.Length);
        Assert.True(Guid.TryParseExact(loaded.Id, "N", out _));
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(JournalKind.Memory, loaded.Kind);
        Assert.Equal("standby", loaded.Target);
        Assert.Equal("-", loaded.PreviousValue);
        Assert.Equal("1048576", loaded.AppliedValue);
        Assert.False(loaded.Restored);
        Assert.True((DateTimeOffset.UtcNow - loaded.AppliedAtUtc).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void JournalDocument_DefaultsAreSafe()
    {
        var document = new JournalDocument();

        Assert.Equal(32, document.SessionId.Length);
        Assert.True(Guid.TryParseExact(document.SessionId, "N", out _));
        Assert.False(document.Active);
        Assert.Empty(document.Entries);
        Assert.Null(document.GameName);
        Assert.Null(document.LastUpdatedUtc);
        Assert.True((DateTimeOffset.UtcNow - document.StartedAtUtc).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void JournalDocument_RoundTripsNullableValues()
    {
        var updated = DateTimeOffset.UtcNow.AddMinutes(-5);
        var document = new JournalDocument
        {
            GameName = null,
            LastUpdatedUtc = updated,
            Entries = [JournalEntry.Create(JournalKind.Dns, "adapter-1", "dns changed", "dhcp", "1.1.1.1,1.0.0.1")]
        };

        var loaded = FusionJson.Deserialize<JournalDocument>(FusionJson.Serialize(document));

        Assert.NotNull(loaded);
        Assert.Null(loaded!.GameName);
        Assert.NotNull(loaded.LastUpdatedUtc);
        Assert.Equal(updated.ToUnixTimeMilliseconds(), loaded.LastUpdatedUtc!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(JournalKind.Dns, loaded.Entries[0].Kind);
    }
}


