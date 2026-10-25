# FusionNet Pro — UI Guide (for page authors)

Read `docs/TEAM_SPEC.md` first. This guide tells you exactly how to build a page that looks and behaves like the rest of the app.

## 1. How pages are discovered

The shell scans the `FusionNet.App` assembly at startup for non-abstract `UserControl`s deriving from `PageBase` with a `[PageInfo]` attribute. **You never touch navigation code.** Add a page = add two files (view + view model).

```csharp
// src/FusionNet.App/Views/Dashboard/DashboardView.xaml.cs
using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.App.ViewModels;

namespace FusionNet.App.Views.Dashboard;

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

    public override void OnSettingsChanged(FusionNet.Core.Models.FusionSettings settings) =>
        _viewModel.OnSettingsChanged(settings);
}
```

Order values: Dashboard 10, Gaming 20, AppShield 30 (group `Nav.Group.Control`); Tuner 40, Dns 50 (group `Nav.Group.Optimize`); Monitor 60, Activity 70 (group `Nav.Group.Insights`); Settings 80 (group `Nav.Group.System`).

## 2. XAML skeleton (copy this)

```xml
<UserControl x:Class="FusionNet.App.Views.Dashboard.DashboardView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:FusionNet.App.Localization"
             xmlns:ctrl="clr-namespace:FusionNet.App.Controls"
             Background="Transparent"
             FlowDirection="{Binding Source={x:Static loc:Loc.Instance}, Path=FlowDirection}">
    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Padding="0,0,8,0">
        <StackPanel Margin="0,0,0,26">

            <!-- page header -->
            <Grid Margin="0,0,0,18">
                <StackPanel>
                    <TextBlock Style="{StaticResource Text.PageTitle}" Text="{loc:T Page.Dashboard}" />
                    <TextBlock Style="{StaticResource Text.PageSubtitle}" Text="{loc:T Page.Dashboard.Subtitle}" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top">
                    <Button Style="{StaticResource Btn.Ghost}" Content="{loc:T Common.Refresh}" />
                </StackPanel>
            </Grid>

            <!-- content: Cards, grids, lists -->
            <Border Style="{StaticResource Card}">
                <TextBlock Style="{StaticResource Text.Body}" Text="{loc:T Some.Key}" />
            </Border>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

- Always `Background="Transparent"` on the root and bind `FlowDirection` exactly as above (Arabic RTL must mirror automatically).
- Content area already has 26 px padding; use margins between cards (`Margin="0,0,0,16"`).
- Use `Grid` with column/row definitions for layouts, `WrapPanel` for chips/metrics, `ItemsControl` for lists (avoid `DataGrid`).

## 3. Theme resource keys (use these, do not hardcode hex colours)

| Purpose | Keys |
|---|---|
| Backgrounds | `Brush.Bg`, `Brush.BgDeep`, `Brush.Window`, `Brush.Sidebar`, `Brush.Hero`, `Brush.Card` |
| Panels / surfaces | `Brush.Surface`, `Brush.SurfaceAlt`, `Brush.SurfaceHover` |
| Borders | `Brush.Border`, `Brush.BorderStrong` |
| Text | `Brush.Text`, `Brush.TextMuted`, `Brush.TextDim` |
| Accent | `Brush.Accent`, `Brush.Accent2`, `Brush.AccentSoft`, `Brush.AccentFaint`, `Brush.AccentBorder`, `Brush.AccentGradient`, `Brush.AccentGradientHorizontal`, `Brush.AccentInk` |
| Status | `Brush.Success`, `Brush.SuccessSoft`, `Brush.SuccessBorder`, `Brush.SuccessGradient`, `Brush.Warning`, `Brush.WarningSoft`, `Brush.WarningBorder`, `Brush.Danger`, `Brush.DangerSoft`, `Brush.DangerBorder`, `Brush.DangerGradient`, `Brush.Info`, `Brush.InfoSoft`, `Brush.InfoBorder`, `Brush.Violet`, `Brush.VioletSoft` |
| Fonts | `Font.UI`, `Font.Display`, `Font.Mono`, `Font.Icon` |
| Text styles | `Text.PageTitle`, `Text.PageSubtitle`, `Text.SectionTitle`, `Text.Eyebrow`, `Text.CardTitle`, `Text.Body`, `Text.Muted`, `Text.Caption`, `Text.Metric`, `Text.MetricSmall`, `Text.Mono`, `Text.Accent`, `Text.Hyperlink`, `Text.Brand` |
| Icons (TextBlock) | `Icon.Sm`, `Icon.Md`, `Icon.Lg`, `Icon.Xl`, `Icon.Accent` + glyphs via `Text="{StaticResource GlyphSearch}"` (keys: `GlyphChevronDown`, `GlyphCheck`, `GlyphClose`, `GlyphMinimize`, `GlyphMaximize`, `GlyphRestore`, `GlyphAdmin`, `GlyphWarning`, `GlyphError`, `GlyphInfo`, `GlyphChevronRight`, `GlyphAdd`, `GlyphDelete`, `GlyphRefresh`, `GlyphSearch`, `GlyphPlay`, `GlyphStop`, `GlyphPower`, `GlyphShield`, `GlyphClock`, `GlyphDownload`, `GlyphUpload`, `GlyphGlobe`, `GlyphGame`, `GlyphSettings`, `GlyphActivity`, `GlyphMonitor`, `GlyphTuner`, `GlyphDashboard`, `GlyphCopy`, `GlyphSave`, `GlyphEdit`) |
| Cards | `Card`, `Card.Hero`, `Card.Flat`, `Panel` |
| Pills | `Pill`, `Pill.Accent`, `Pill.Success`, `Pill.Warning`, `Pill.Danger` |
| Buttons | `Btn.Primary`, `Btn.Ghost`, `Btn.Soft`, `Btn.Danger`, `Btn.Success`, `Btn.Link`, `Btn.Icon` (set FontFamily icon + Content glyph), `Btn.Chip` (ToggleButton), `Btn.Nav` (shell only) |
| Inputs | `Input.TextBox`, `Input.SearchBox`, `Input.ComboBox`, `Input.CheckBox`; implicit styles: `ToggleSwitch` (`ctrl:ToggleSwitch`), `Slider`, `ProgressBar`, `ListBox`/`ListBoxItem`, `ScrollBar` |
| Converters (App resources) | `BoolToVis`, `InverseBool`, `InverseBoolToVis`, `NullToVis`, `NotNullToVis`, `EmptyToVis`, `CountToVis` |

Custom graphics: `ctrl:StatusRing` (DPs `Progress` 0-1, `Thickness`, `TrackBrush`, `ProgressBrush`, `StartAngle`) and `ctrl:Sparkline` (DPs `Values` `IEnumerable<double>`, `Stroke`, `Fill`, `StrokeThickness`, `MinimumRange`).

Never hardcode `#22D3EE`; use accent brushes so the user's accent choice works.

## 4. Localization

Register page strings once, in a separate file next to the view (module initializer runs before startup):

```csharp
using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Dashboard;

internal static class DashboardStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        Loc.Add("Dash.PingNow", "Ping now", "قِس الآن");
        // ...
    }
}
```

Use them in XAML with `Text="{loc:T Dash.PingNow}"`; in code `Loc.T("Dash.PingNow")` / `Loc.Format("Dash.Value", value)`.

Rules: natural professional Arabic (not machine-literal), keep technical tokens (DNS, TCP, FPS, ms, Mbps, Windows) in Latin, numbers in Latin digits. Every user-visible string must exist in both languages. Prefix all keys with the page (`Dash.`, `Game.`, `Shield.`, `Tuner.`, `Dns.`, `Mon.`, `Log.`, `Set.`) so pages never collide.

## 5. View model rules

- Derive from `ViewModelBase` (`FusionNet.App.Mvvm`), implement `INotifyPropertyChanged` via `SetProperty`.
- Constructor takes `IAppServices services`; store it (`protected IAppServices S`).
- Use `RelayCommand` / `AsyncRelayCommand` from `FusionNet.App.Mvvm` for all actions.
- **Services raise events on background threads.** In `ActivateAsync()` subscribe and wrap handlers:

```csharp
S.Fusion.SnapshotChanged += OnSnapshot;             // background thread
private void OnSnapshot(object? sender, FusionSnapshot snapshot) =>
    UiDispatcher.Post(() => ApplySnapshot(snapshot));
```

- In `Deactivate()` **always unsubscribe** every handler and stop loops (pages are cached and re-activated).
- Save settings with `S.SaveSettings()`; read them from `S.Settings`. After changing settings also raise your own properties.
- Show feedback with `ToastService.Instance.Show(message, ToastKind.Success/Warning/Error)`; localized variant `ShowLocalized("Key")`.
- Never block: `await` service calls; no `.Result`, no `Thread.Sleep`.

## 6. Services cheat-sheet (`IAppServices`)

| Area | Member | Notes |
|---|---|---|
| Settings | `Settings`, `SaveSettings()`, `ReloadSettings()` | `FusionSettings`, normalized on save |
| Fusion mode | `Fusion` | `EngageAsync()`, `DisengageAsync()`, `RefreshAsync()`, `StateChanged`, `SnapshotChanged`, `Snapshot`, `ApplyTweaksAsync/RevertTweaksAsync` |
| Games | `Games` | `Changed` event, `CurrentGame`, `RunningGames`, `SetWatchList`, `PollNow` |
| Ping | `Ping` | `Start()`, `Stop()`, `SetTargets()`, `Statistics`, `Tick` |
| Throughput | `Throughput` | `Start()`, `Stop()`, `Latest`, `SampleReceived` |
| Adapters | `Adapters` | `GetAdapters()`, `GetDefaultAdapter()` |
| DNS | `Dns` | `BenchmarkAsync(providers, progress, ct)`, `ApplyServers`, `ResetToAutomatic`, `FlushCache` |
| Public IP | `PublicIp` | `GetAsync(ct)` |
| Diagnostics | `Diagnostics` | `RunAsync(ct)`, `BuildTextReport(report)` |
| Processes | `Processes` | `GetProcesses()`, suspend/throttle/boost |
| Services | `Services` | pause/start Windows services |
| Tweaks | `Tweaks` | `ReadAll`, `Apply`, `Revert` |
| Journal | `Journal` | session entries, `HasPendingRestore` |
| Activity | `Activity` | `EntryAdded`, `Snapshot()`, `ExportText()` |
| Elevation | `Elevation` | `IsElevated`, `TryRestartElevated()` |
| Files | `Files` | `PickSavePath(name, filter)`, `PickOpenPath(filter)` |
| System | `SystemInfo` | Windows version, CPU, RAM, uptime, app version |

For app-specific services (tray, hotkey, accent) use `FusionNet.App.Services.TrayService.Instance`, `HotkeyService.Instance`, `ThemeService`, `ToastService.Instance`.

## 7. Quality bar for pages

- Pixel-consistent with the shell: 16 px gaps, 14-18 px padding, `CornerRadius` from styles, icons from glyph resources.
- Every interactive element has a live effect; every dangerous action asks for confirmation.
- Show meaningful empty states (`Label.EmptyTitle` / `Label.EmptyBody`).
- Show the elevation requirement inline (`Pill.Warning` + "Restart as administrator") instead of failing silently.
- Numbers use `FusionNet.Core.Utilities.Humanize` (`Humanize.Ms`, `Humanize.Bytes`, `Humanize.Rate`, `Humanize.Speed`, `Humanize.Percent`).
- Complex pages must still render correctly at 1280×800 and at 2560×1440.
- Build with zero errors and zero new warnings; verify `FlowDirection` by toggling language.

## 8. Build & run

```powershell
cd FusionNetPro   # repository root
dotnet build src\FusionNet.App -v q --nologo
```

The Lead wires services and runs the app; if the app cannot start because another subsystem is still in progress, rely on the XAML compiler (`dotnet build`) plus careful visual review.
