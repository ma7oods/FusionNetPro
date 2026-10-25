using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Tuner;

/// <summary>Network Tuner page: grouped, reversible Windows latency tweaks.</summary>
[PageInfo("tuner", "Page.Tuner", Glyphs.Tuner, order: 40, groupKey: "Nav.Group.Optimize")]
public partial class TunerView : PageBase
{
    private readonly TunerViewModel _viewModel;

    public TunerView()
    {
        InitializeComponent();
        _viewModel = new TunerViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged();
}
