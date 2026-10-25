using System.Runtime.CompilerServices;
using FusionNet.App.Localization;
using FusionNet.App.Services;
using FusionNet.App.Shell;
using FusionNet.App.Styles;

namespace FusionNet.App.Views.Tools;

// Gaming-only build: Tools page kept in source but removed from navigation.
public partial class ToolsView : PageBase
{
    public ToolsView():this(new ToolsViewModel(AppServices.Current)){}
    public ToolsView(object model){InitializeComponent();DataContext=model;}
    public override Task OnActivatedAsync(){if(DataContext is ToolsViewModel model)model.Activate();return Task.CompletedTask;}
    public override void OnDeactivated(){if(DataContext is ToolsViewModel model)model.Deactivate();}
}
internal static class ToolStrings
{
    [ModuleInitializer] internal static void Register()=>Loc.Add("Page.Tools","Tools & hardware","أدوات التشخيص والعتاد");
}
