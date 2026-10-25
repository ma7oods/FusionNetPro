using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.AppShield;

/// <summary>App Shield page — per-application network policy for everything running in the background.</summary>
[PageInfo("appshield", "Page.AppShield", Glyphs.Shield, order: 30, groupKey: "Nav.Group.Control")]
public partial class AppShieldView : PageBase
{
    private readonly AppShieldViewModel _viewModel;

    public AppShieldView()
    {
        InitializeComponent();
        _viewModel = new AppShieldViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged(settings);
}
