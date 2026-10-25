using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Tests;

public class ReadOnlyStartupTests
{
    [Fact] public void NewSettingsDoNotAuthorizeAutomaticSystemChanges() => Assert.False(new FusionSettings().AutoEngageOnGameStart);
    [Fact] public void PreviouslySavedPreferenceIsNotSilentlyOverwritten()
    {
        var settings = FusionJson.Deserialize<FusionSettings>("{\"autoEngageOnGameStart\":true}")!;
        settings.Normalize();
        Assert.True(settings.AutoEngageOnGameStart);
    }
}
