using FusionNet.Core.Utilities;

namespace FusionNet.Tests;

/// <summary>Hotkey parsing, formatting, round-tripping and Win32 virtual-key mapping.</summary>
public class HotkeyGestureTests
{
    [Fact]
    public void Default_IsCtrlAltF()
    {
        var gesture = HotkeyGesture.Default;

        Assert.True(gesture.Ctrl);
        Assert.True(gesture.Alt);
        Assert.False(gesture.Shift);
        Assert.False(gesture.Win);
        Assert.Equal("F", gesture.Key);
        Assert.True(gesture.HasModifier);
        Assert.Equal("Ctrl+Alt+F", gesture.ToString());
        Assert.Equal("Ctrl + Alt + F", gesture.DisplayString);
    }

    [Theory]
    [InlineData("Ctrl+Alt+F", true, true, false, false, "F")]
    [InlineData("ctrl+alt+f", true, true, false, false, "F")]
    [InlineData("Ctrl+Shift+S", true, false, true, false, "S")]
    [InlineData("Control+Alt+1", true, true, false, false, "1")]
    [InlineData("Win+Shift+F1", false, false, true, true, "F1")]
    [InlineData("Alt+F12", false, true, false, false, "F12")]
    [InlineData("F+Ctrl", true, false, false, false, "F")]
    [InlineData("Ctrl+Alt+PageUp", true, true, false, false, "PageUp")]
    [InlineData("Ctrl+Alt+Space", true, true, false, false, "Space")]
    public void TryParse_AcceptsValidGestures(
        string text, bool ctrl, bool alt, bool shift, bool win, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture), $"'{text}' should parse.");
        Assert.Equal(ctrl, gesture.Ctrl);
        Assert.Equal(alt, gesture.Alt);
        Assert.Equal(shift, gesture.Shift);
        Assert.Equal(win, gesture.Win);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Ctrl+Alt+F13")]
    [InlineData("Ctrl+Alt+A+B")]
    [InlineData("Ctrl+Alt+")]
    public void TryParse_RejectsInvalidGestures(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_ReturnsDefaultGestureOnFailure()
    {
        Assert.False(HotkeyGesture.TryParse("garbage", out var gesture));
        Assert.Equal(HotkeyGesture.Default, gesture);
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse()
    {
        var originals = new[]
        {
            new HotkeyGesture(true, true, false, false, "F"),
            new HotkeyGesture(true, false, true, false, "S"),
            new HotkeyGesture(false, false, false, true, "F12"),
            new HotkeyGesture(true, true, true, true, "8")
        };

        foreach (var original in originals)
        {
            Assert.True(HotkeyGesture.TryParse(original.ToString(), out var parsed));
            Assert.Equal(original, parsed);
            Assert.Equal(original.ToString(), parsed.ToString());
        }
    }

    [Fact]
    public void ToString_JoinsModifiersCanonically()
    {
        var gesture = new HotkeyGesture(true, true, true, true, "A");
        Assert.Equal("Ctrl+Alt+Shift+Win+A", gesture.ToString());
        Assert.Equal("Ctrl + Alt + Shift + Win + A", gesture.DisplayString);

        var noModifiers = new HotkeyGesture(false, false, false, false, "F5");
        Assert.False(noModifiers.HasModifier);
        Assert.Equal("F5", noModifiers.ToString());
    }

    [Theory]
    [InlineData("A", 0x41u)]
    [InlineData("Z", 0x5Au)]
    [InlineData("0", 0x30u)]
    [InlineData("9", 0x39u)]
    [InlineData("F1", 0x70u)]
    [InlineData("F12", 0x7Bu)]
    public void ToVirtualKey_MapsLettersDigitsAndFunctionKeys(string key, uint expected)
    {
        var gesture = new HotkeyGesture(true, false, false, false, key);
        Assert.Equal(expected, gesture.ToVirtualKey());
    }

    [Fact]
    public void ToVirtualKey_ReturnsZeroForUnsupportedKeys()
    {
        Assert.Equal(0u, new HotkeyGesture(true, false, false, false, "F13").ToVirtualKey());
        Assert.Equal(0u, new HotkeyGesture(true, false, false, false, "Space").ToVirtualKey());
        Assert.Equal(0u, new HotkeyGesture(true, false, false, false, "PageUp").ToVirtualKey());
        Assert.Equal(0u, new HotkeyGesture(true, false, false, false, "").ToVirtualKey());
    }

    [Fact]
    public void ModifierParts_OnlyListsPressedModifiers()
    {
        Assert.Equal(new[] { "Ctrl", "Alt" }, new HotkeyGesture(true, true, false, false, "F").ModifierParts());
        Assert.Equal(new[] { "Win" }, new HotkeyGesture(false, false, false, true, "F").ModifierParts());
        Assert.Empty(new HotkeyGesture(false, false, false, false, "F").ModifierParts());
    }

    [Fact]
    public void AppPaths_ExposeStableLocations()
    {
        Assert.Equal("FusionNet Pro", AppPaths.ProductName);
        Assert.Equal("FusionNetPro", AppPaths.Slug);
        Assert.Equal("FusionNetPro.exe", AppPaths.ExecutableName);

        Assert.Equal("FusionNetPro", Path.GetFileName(AppPaths.DataDirectory));
        Assert.True(Path.IsPathRooted(AppPaths.DataDirectory));
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "settings.json"), AppPaths.SettingsFile);
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "journal.json"), AppPaths.JournalFile);
        Assert.EndsWith("logs", AppPaths.LogDirectory);
        Assert.EndsWith("reports", AppPaths.ReportDirectory);
        Assert.EndsWith("crashes", AppPaths.CrashDirectory);
        Assert.EndsWith("backups", AppPaths.ExportedSettingsDirectory);
    }
}
