using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using FusionNet.App.Localization;
using FusionNet.App.Services;

namespace FusionNet.App.Controls;

/// <summary>
/// Transparent overlay that renders the process-wide <see cref="ToastService"/> queue as
/// stacked, kind-coloured cards. Cards fade/slide in on load and dismiss on click; the rest
/// of the overlay is not hit-test visible, so it never blocks the page underneath.
/// </summary>
public partial class ToastHost
{
    public ToastHost()
    {
        InitializeComponent();

        // The queue is a singleton owned by ToastService: bind it directly so the host stays
        // stateless, keeps rendering through shell re-parenting and needs no disposal logic.
        ItemsList.ItemsSource = ToastService.Instance.Items;
    }

    private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not FrameworkElement { DataContext: ToastItem item }) return;

        ToastService.Instance.Dismiss(item);
        e.Handled = true;
    }
}

/// <summary>Strings owned by the toast host (registered before app startup).</summary>
internal static class ToastStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        Loc.Add("Toast.DismissHint", "Click to dismiss", "انقر للإغلاق");
    }
}
