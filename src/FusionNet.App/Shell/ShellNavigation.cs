namespace FusionNet.App.Shell;

/// <summary>Lets pages ask the shell to switch page (e.g. "View all" → Activity log).</summary>
public static class ShellNavigation
{
    public static event Action<string>? NavigateRequested;

    public static void Go(string pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey)) return;
        NavigateRequested?.Invoke(pageKey);
    }
}
