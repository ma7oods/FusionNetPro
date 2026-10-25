using System.Windows;
using System.Windows.Input;
using FusionNet.App.Localization;
using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Settings;

/// <summary>Settings page: appearance, behaviour, hotkey, Fusion defaults, data and about.</summary>
[PageInfo("settings", "Page.Settings", Glyphs.Settings, order: 80, groupKey: "Nav.Group.System")]
public partial class SettingsView : PageBase
{
    private readonly SettingsViewModel _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(AppServices.Current);
        DataContext = _viewModel;
    }

    public override Task OnActivatedAsync() => _viewModel.ActivateAsync();

    public override void OnDeactivated() => _viewModel.Deactivate();

    public override void OnSettingsChanged(FusionSettings settings) => _viewModel.OnSettingsChanged(settings);

    private void OnRecordHotkeyClick(object sender, RoutedEventArgs e)
    {
        _viewModel.BeginRecording();
        HotkeyCapture.Focus();
        Keyboard.Focus(HotkeyCapture);
    }

    private void OnHotkeyCapturePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsRecording) return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _viewModel.CancelRecording();
            return;
        }

        // Wait for the real key; modifier-only presses are not a gesture yet.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var hasModifier = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt
            | ModifierKeys.Shift | ModifierKeys.Windows)) != 0;
        if (!hasModifier)
        {
            _viewModel.ReportHotkeyError(Loc.T("Set.HotkeyNeedsModifier"));
            return;
        }

        var keyName = MapKey(key);
        if (keyName is null)
        {
            _viewModel.ReportHotkeyError(Loc.T("Set.HotkeyUnsupported"));
            return;
        }

        var gesture = new HotkeyGesture(
            Ctrl: modifiers.HasFlag(ModifierKeys.Control),
            Alt: modifiers.HasFlag(ModifierKeys.Alt),
            Shift: modifiers.HasFlag(ModifierKeys.Shift),
            Win: modifiers.HasFlag(ModifierKeys.Windows),
            Key: keyName);

        _viewModel.ApplyHotkey(gesture, Window.GetWindow(this));
    }

    /// <summary>Maps a WPF key to the tokens understood by <see cref="HotkeyGesture"/>.</summary>
    private static string? MapKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key is >= Key.F1 and <= Key.F12) return "F" + (key - Key.F1 + 1);

        return null;
    }
}
