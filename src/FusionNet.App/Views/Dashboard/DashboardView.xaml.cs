using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Dashboard;

/// <summary>Dashboard page — one switch that protects the connection and focuses the machine.</summary>
[PageInfo("dashboard", "Page.Dashboard", Glyphs.Dashboard, order: 10, groupKey: "Nav.Group.Control")]
public partial class DashboardView : PageBase
{
    private readonly DashboardViewModel _viewModel;

    public DashboardView()
    {
        InitializeComponent();
        _viewModel = new DashboardViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged(settings);
}
