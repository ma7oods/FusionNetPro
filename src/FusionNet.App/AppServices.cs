using FusionNet.Core.Abstractions;

namespace FusionNet.App;

/// <summary>Ambient access to the composed application services for views.</summary>
public static class AppServices
{
    public static IAppServices Current { get; private set; } = null!;

    public static bool IsReady => Current is not null;

    public static void Initialize(IAppServices services) => Current = services;
}
