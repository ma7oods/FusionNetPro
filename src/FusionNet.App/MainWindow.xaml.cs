using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.App.Styles;
using FusionNet.App.ViewModels;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App;

public partial class MainWindow : Window
{
    private readonly IAppServices _services;
    private readonly MainViewModel _viewModel;
    private bool _forceClose;
    private bool _closing;
    private bool _trayHintShown;
    private bool _automationAllowed;

    public MainWindow()
    {
        _services = AppServices.Current ?? throw new InvalidOperationException("Services must be initialized before the main window.");
        _viewModel = new MainViewModel(_services);

        InitializeComponent();
        DataContext = _viewModel;
        PageHost.RenderTransform = new TranslateTransform();

        _viewModel.PageChanged += OnPageChanged;
        StateChanged += OnWindowStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        _services.Fusion.SnapshotChanged += OnFusionSnapshotChanged;
        _services.SettingsChanged += OnSettingsChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Called by App once the window is visible, so tray/hotkey/monitors have a handle.</summary>
    public async Task InitializeRuntimeAsync(bool armAutomation = true)
    {
        try
        {
            TrayService.Instance.Initialize(this, RestoreFromTray, ToggleFusionFromTray, OpenSettings);
            HotkeyService.Instance.Pressed += OnHotkeyPressed;
            RegisterHotkey(showError: false);

            var settings = _services.Settings;
            _services.Ping.SetTargets(settings.PingTargets);
            _services.Ping.IntervalMs = settings.PingIntervalMs;
            _services.Ping.TimeoutMs = settings.PingTimeoutMs;
            if (settings.LiveMonitoring) _services.Ping.Start();
            _services.Throughput.Start();
            _services.Games.Start();

            // Recover a crashed session before automation can re-arm (needs admin; no-ops otherwise).
            if (_services.Journal.HasPendingRestore)
                await RestoreInterruptedSessionAsync().ConfigureAwait(true);

            // Game Boost state must be loaded first so auto-engage sees a pending Game Boost session.
            await _services.GameBoost.RefreshAsync(default);
            // The owner's opt-in toggle authorizes automatic engage on game start (needs admin to act).
            _automationAllowed = armAutomation;
            SyncAutomation(settings);
            UpdateTrayState(_services.Fusion.Snapshot);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    public void NavigateToPage(string pageKey) => _viewModel.Navigate(pageKey);

    // ------------------------------------------------------------------ window commands
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var text = Loc.Instance.IsArabic
            ? "فيوجن نت برو\n\n• شغّل وضع Fusion من الشريط الجانبي لحماية اتصالك أثناء اللعب.\n• اختر لعبتك من صفحة الألعاب، وحدّد التطبيقات الخلفية من درع التطبيقات.\n• كل تغيير يُسجَّل ويُستعاد تلقائياً عند إيقاف الوضع.\n• وضع Fusion يحتاج صلاحيات المسؤول: استخدم زر إعادة التشغيل كمسؤول.\n\nاختصارات لوحة المفاتيح:\nCtrl+1..9  التنقل بين الصفحات\nCtrl+Alt+F  تشغيل/إيقاف وضع Fusion\nF1  هذه المساعدة"
            : "FusionNet Pro\n\n• Activate Fusion Mode from the sidebar to protect your connection while gaming.\n• Pick your game on the Games page and choose background apps on App Shield.\n• Every change is journaled and restored automatically when you stop.\n• Fusion Mode needs administrator rights: use the Restart as administrator button.\n\nKeyboard shortcuts:\nCtrl+1..9  switch pages\nCtrl+Alt+F  toggle Fusion Mode\nF1  this help";
        MessageBox.Show(this, text, Loc.T("TitleBar.Help"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F1)
        {
            HelpButton_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control &&
            e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
        {
            var index = e.Key - System.Windows.Input.Key.D1;
            var keys = _viewModel.NavGroups.SelectMany(group => group.Items).Select(item => item.Key).ToArray();
            if (index < keys.Length)
            {
                _viewModel.Navigate(keys[index]);
                e.Handled = true;
            }
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? Glyphs.Restore : Glyphs.Maximize;

        if (WindowState == WindowState.Minimized && _services.Settings.MinimizeToTray)
        {
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                TrayService.Instance.ShowNotification(Loc.T("App.Name"), Loc.T("Tray.RunningHint"));
            }
        }
    }

    // ------------------------------------------------------------------ page transition
    private void OnPageChanged(object? sender, EventArgs e)
    {
        if (PageHost.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            PageHost.RenderTransform = transform;
        }

        PageHost.Opacity = 0;
        transform.Y = 10;

        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    // ------------------------------------------------------------------ tray / hotkey glue
    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    // While closing, a new session could start after the exit restore and outlive the app.
    private void ToggleFusionFromTray()
    {
        if (!_closing) _viewModel.ToggleFusionCommand.Execute(null);
    }

    private void OpenSettings() => _viewModel.Navigate("settings");

    private void OnHotkeyPressed(object? sender, EventArgs e) => ToggleFusionFromTray();

    private void RegisterHotkey(bool showError)
    {
        if (!HotkeyGesture.TryParse(_services.Settings.Hotkey, out var gesture)) return;

        if (!HotkeyService.Instance.Register(this, gesture, out var error) && showError && !string.IsNullOrWhiteSpace(error))
            ToastService.Instance.Show(error, ToastKind.Warning);
    }

    private void OnFusionSnapshotChanged(object? sender, FusionSnapshot snapshot)
    {
        UiDispatcher.Post(() => UpdateTrayState(snapshot));
    }

    private void UpdateTrayState(FusionSnapshot snapshot)
    {
        var active = snapshot.State is FusionModeState.Active or FusionModeState.Arming;
        TrayService.Instance.UpdateState(active, snapshot.Game?.Name);
    }

    private void OnSettingsChanged(object? sender, FusionSettings settings) =>
        UiDispatcher.Post(() =>
        {
            RegisterHotkey(showError: true);
            SyncAutomation(settings);
        });

    // StartAutomation engages at once if a game is already running, so it must follow the toggle.
    private void SyncAutomation(FusionSettings settings)
    {
        if (_automationAllowed && !_closing && settings.AutoEngageOnGameStart) _services.Fusion.StartAutomation();
        else _services.Fusion.StopAutomation();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        UiDispatcher.Post(() => TrayService.Instance.UpdateState(
            _services.Fusion.Snapshot.State is FusionModeState.Active or FusionModeState.Arming,
            _services.Fusion.Snapshot.Game?.Name));

    // ------------------------------------------------------------------ lifetime
    private async Task RestoreInterruptedSessionAsync()
    {
        try
        {
            var result = await _services.Fusion.RestoreInterruptedSessionAsync().ConfigureAwait(true);
            ToastService.Instance.Show(result.Message, result.Success ? ToastKind.Success : ToastKind.Warning);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        // No new session may start between the state snapshot below and the restore.
        _services.Fusion.StopAutomation();
        var keepOpen = false;

        try
        {
            if (_services.GameBoost.Status.IsPending)
            {
                await _services.GameBoost.StopAsync(default);
                for (var attempt = 0; attempt < 15; attempt++)
                {
                    if (!(await _services.GameBoost.RefreshAsync(default)).IsPending) break;
                    await Task.Delay(1000);
                }
                if (_services.GameBoost.Status.IsPending)
                    ToastService.Instance.Show("استرجاع Game Boost ما زال معلقًا؛ ستبقى المهمة المستقلة للمحاولة.", ToastKind.Warning);
            }
            // Only a live/recovering session (or a crash-recovery journal) can lose restoration
            // work on exit. Automation merely being "armed" in STANDBY must NOT show the scary
            // "Fusion Mode is still active" prompt on every close.
            var state = _services.Fusion.State;
            var sessionActive = state is FusionModeState.Active or FusionModeState.Arming or FusionModeState.Restoring;
            var pendingRestore = _services.Journal.HasPendingRestore;
            var needsRestore = sessionActive || pendingRestore;

            // Without admin nothing can be restored now, so a prompt promising a restore would be false.
            if (needsRestore && _services.Settings.ConfirmExitWhileActive && _services.Elevation.IsElevated)
            {
                var answer = MessageBox.Show(this, Loc.T("Exit.Body"), Loc.T("Exit.Title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    keepOpen = true;
                    _closing = false;
                    SyncAutomation(_services.Settings);
                    return;
                }
            }

            // From here the app exits: block in-window Engage clicks while the restore runs.
            IsEnabled = false;
            if (needsRestore)
            {
                var result = sessionActive
                    ? await _services.Fusion.DisengageAsync().ConfigureAwait(true)
                    : await _services.Fusion.RestoreInterruptedSessionAsync().ConfigureAwait(true);
                if (!result.Success)
                    ToastService.Instance.Show(result.Message, ToastKind.Warning);
            }

            // Give the coordinator a chance to cancel/await its reconciler before the process ends.
            try
            {
                await _services.Fusion.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Coordinator dispose failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shell] Shutdown restore failed: {ex}");
        }
        finally
        {
            if (!keepOpen)
            {
                Cleanup();
                _forceClose = true;
                // Close() throws while WPF is still inside this Closing event (no real await happened).
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }

    private void Cleanup()
    {
        try
        {
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            _services.Fusion.SnapshotChanged -= OnFusionSnapshotChanged;
            _services.SettingsChanged -= OnSettingsChanged;
            HotkeyService.Instance.Pressed -= OnHotkeyPressed;
            HotkeyService.Instance.Unregister();
            TrayService.Instance.Dispose();
            _services.Ping.Stop();
            _services.Throughput.Stop();
            _services.Games.Stop();
            _services.Fusion.StopAutomation();
            _viewModel.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shell] Cleanup failed: {ex}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Cleanup();
        Application.Current.Shutdown(0);
    }
}
