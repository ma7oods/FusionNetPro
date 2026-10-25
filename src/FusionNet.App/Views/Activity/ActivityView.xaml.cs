using System.Windows.Threading;
using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Activity;

/// <summary>Activity Log page: live, filterable history plus the session journal.</summary>
[PageInfo("activity", "Page.Activity", Glyphs.Activity, order: 70, groupKey: "Nav.Group.Insights")]
public partial class ActivityView : PageBase
{
    private readonly ActivityViewModel _viewModel;

    public ActivityView()
    {
        InitializeComponent();
        _viewModel = new ActivityViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override async Task OnActivatedAsync()
    {
        // Pages are cached, so (re)attach the scroll bridge on every activation.
        _viewModel.ScrollToTopRequested -= OnScrollToTopRequested;
        _viewModel.ScrollToTopRequested += OnScrollToTopRequested;
        await _viewModel.ActivateAsync();
    }

    public override void OnDeactivated()
    {
        _viewModel.ScrollToTopRequested -= OnScrollToTopRequested;
        _viewModel.Deactivate();
    }

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged();

    private void OnScrollToTopRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (EntryList.Items.Count > 0) EntryList.ScrollIntoView(EntryList.Items[0]);
        }, DispatcherPriority.Background);
}
