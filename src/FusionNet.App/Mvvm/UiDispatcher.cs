using System.Windows;
using System.Windows.Threading;

namespace FusionNet.App.Mvvm;

/// <summary>Marshals service callbacks (which arrive on background threads) to the UI thread.</summary>
public static class UiDispatcher
{
    public static Dispatcher? Dispatcher =>
        Application.Current?.Dispatcher is { HasShutdownStarted: false } dispatcher ? dispatcher : null;

    public static void Post(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    public static void Invoke(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    public static void RunOnUi(Action action) => Post(action);
}
