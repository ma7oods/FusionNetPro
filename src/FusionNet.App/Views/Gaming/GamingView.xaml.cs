using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Gaming;

/// <summary>Games &amp; Profiles page — detection mode, game catalog, custom games and automation.</summary>
[PageInfo("gaming", "Page.Gaming", Glyphs.Game, order: 20, groupKey: "Nav.Group.Control")]
public partial class GamingView : PageBase
{
    private readonly GamingViewModel _viewModel;

    public GamingView()
    {
        InitializeComponent();
        _viewModel = new GamingViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged(settings);
}
