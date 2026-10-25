using System.Windows;

namespace FusionNet.App.Shell;

/// <summary>Shell-level requests raised from pages (settings page, help links).</summary>
public static class ShellEvents
{
    /// <summary>Ask the shell to show the first-run welcome guide again.</summary>
    public static event EventHandler? OnboardingRequested;

    /// <summary>Ask the shell to open the help / shortcuts overlay.</summary>
    public static event EventHandler? HelpRequested;

    public static void RequestOnboarding() => OnboardingRequested?.Invoke(null, EventArgs.Empty);

    public static void RequestHelp() => HelpRequested?.Invoke(null, EventArgs.Empty);

    /// <summary>Raised by the shell when the welcome guide is finished or skipped.</summary>
    public static event EventHandler? OnboardingCompleted;

    public static void NotifyOnboardingCompleted() => OnboardingCompleted?.Invoke(null, EventArgs.Empty);
}
