using System.Windows.Input;
using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Monitor;

/// <summary>Live Monitor page: adapter health, latency, throughput, targets and diagnostics.</summary>
[PageInfo("monitor", "Page.Monitor", Glyphs.Monitor, order: 60, groupKey: "Nav.Group.Insights")]
public partial class MonitorView : PageBase
{
    private readonly MonitorViewModel _viewModel;

    public MonitorView()
    {
        InitializeComponent();
        _viewModel = new MonitorViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged();

    private void OnTargetInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_viewModel.AddTargetCommand.CanExecute(null))
        {
            _viewModel.AddTargetCommand.Execute(null);
        }

        e.Handled = true;
    }
}
