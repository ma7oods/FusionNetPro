using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Dns;

/// <summary>DNS &amp; Latency page: current servers, benchmark, public IP and custom DNS.</summary>
[PageInfo("dns", "Page.Dns", Glyphs.Globe, order: 50, groupKey: "Nav.Group.Optimize")]
public partial class DnsView : PageBase
{
    private readonly DnsViewModel _viewModel;

    public DnsView()
    {
        InitializeComponent();
        _viewModel = new DnsViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged();
}
