using System.Diagnostics;
using System.IO;
using System.Windows;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace FusionNet.App.Services;

/// <summary>
/// Owns the notification-area icon and its context menu. The shell creates exactly one
/// instance through <see cref="Instance"/>, calls <see cref="Initialize"/> once the main
/// window exists and feeds it live state through <see cref="UpdateState"/>.
/// Every WinForms member stays on the WPF dispatcher thread; failures degrade to "no tray"
/// instead of throwing, because a missing tray icon must never take the app down.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const string AppIconPackUri = "pack://application:,,,/Assets/app.ico";

    /// <summary>WinForms refuses balloon/tooltip strings that are too long.</summary>
    private const int TooltipMaxLength = 63;
    private const int BalloonTextMaxLength = 255;
    private const int BalloonTimeoutMs = 6000;

    private static readonly Lazy<TrayService> LazyInstance =
        new(() => new TrayService(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static TrayService Instance => LazyInstance.Value;

    private readonly object _gate = new();

    private Window? _owner;
    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ContextMenuStrip? _menu;
    private WinForms.ToolStripMenuItem? _openItem;
    private WinForms.ToolStripMenuItem? _toggleItem;
    private WinForms.ToolStripMenuItem? _settingsItem;
    private WinForms.ToolStripMenuItem? _exitItem;
    private Drawing.Icon? _icon;
    private Drawing.Font? _menuFont;

    private Action? _openApp;
    private Action? _toggleFusion;
    private Action? _openSettings;

    private bool _fusionActive;
    private string? _gameName;
    private bool _initialized;
    private bool _disposed;

    private TrayService()
    {
    }

    // ==================================================================== public API

    /// <summary>
    /// Creates the tray icon and context menu. Safe to call from any thread; creation is
    /// marshalled to the window dispatcher. A second call refreshes the callbacks/labels.
    /// </summary>
    public void Initialize(Window owner, Action openApp, Action toggleFusion, Action openSettings)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(openApp);
        ArgumentNullException.ThrowIfNull(toggleFusion);
        ArgumentNullException.ThrowIfNull(openSettings);

        // NotifyIcon/ContextMenuStrip are WinForms components: they must be created and
        // disposed on the thread that owns the WPF dispatcher.
        if (!owner.Dispatcher.CheckAccess())
        {
            try
            {
                owner.Dispatcher.Invoke(() => InitializeCore(owner, openApp, toggleFusion, openSettings));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrayService.Initialize could not marshal to the UI thread: {ex}");
            }

            return;
        }

        InitializeCore(owner, openApp, toggleFusion, openSettings);
    }

    /// <summary>Refreshes tooltip + menu labels whenever Fusion state or the detected game changes.</summary>
    public void UpdateState(bool fusionActive, string? gameName)
    {
        lock (_gate)
        {
            _fusionActive = fusionActive;
            _gameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();
        }

        if (_disposed) return;
        UiDispatcher.Post(ApplyStateCore);
    }

    /// <summary>
    /// Windows balloon notification. Silently does nothing when the tray is unavailable or
    /// notifications were never initialized — callers treat this as best-effort UI.
    /// </summary>
    public void ShowNotification(string title, string message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message) || _disposed) return;

        var safeTitle = Fit(string.IsNullOrWhiteSpace(title) ? Loc.T("App.Name") : title, TooltipMaxLength);
        var safeMessage = Fit(message, BalloonTextMaxLength);

        UiDispatcher.Post(() =>
        {
            var icon = _notifyIcon;
            if (icon is null) return;

            try
            {
                icon.ShowBalloonTip(
                    BalloonTimeoutMs,
                    safeTitle,
                    safeMessage,
                    isError ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                // Balloons are unreliable on locked-down or shell-less sessions; ignore.
                Debug.WriteLine($"TrayService.ShowNotification failed: {ex}");
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // WinForms components must be torn down on their creating thread.
        UiDispatcher.Invoke(DisposeCore);
    }

    // ==================================================================== creation

    private void InitializeCore(Window owner, Action openApp, Action toggleFusion, Action openSettings)
    {
        lock (_gate)
        {
            _disposed = false;
            _owner = owner;
            _openApp = openApp;
            _toggleFusion = toggleFusion;
            _openSettings = openSettings;
        }

        if (_initialized)
        {
            RebuildMenuTexts();
            ApplyStateCore();
            return;
        }

        try
        {
            _menuFont = CreateMenuFont();
            var colors = DarkMenuColors.FromTheme();

            _menu = new WinForms.ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Renderer = new DarkMenuRenderer(colors),
                Font = _menuFont,
                Padding = new WinForms.Padding(3, 3, 3, 3)
            };
            _menu.Opening += OnMenuOpening;

            _openItem = CreateMenuItem(() => InvokeAction(_openApp));
            _toggleItem = CreateMenuItem(() => InvokeAction(_toggleFusion));
            _settingsItem = CreateMenuItem(() => InvokeAction(_openSettings));
            _exitItem = CreateMenuItem(RequestExit);

            _menu.Items.Add(_openItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(_toggleItem);
            _menu.Items.Add(_settingsItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(_exitItem);

            _icon = LoadAppIcon();

            FusionNet.Windows.Native.Win32Native.TryAllowTaskbarCreated();
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = _icon,
                Text = BuildTooltip(),
                ContextMenuStrip = _menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += OnTrayDoubleClick;
            _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

            RebuildMenuTexts();
            ApplyStateCore();

            Loc.Instance.LanguageChanged += OnLanguageChanged;
            var app = System.Windows.Application.Current;
            if (app is not null) app.Exit += OnApplicationExit;

            _initialized = true;
        }
        catch (Exception ex)
        {
            // Missing shell, locked-down session, corrupt icon…: run without a tray icon.
            Debug.WriteLine($"TrayService could not create the tray icon: {ex}");
            DisposeCore();
        }
    }

    private static Drawing.Font CreateMenuFont()
    {
        try
        {
            return new Drawing.Font("Segoe UI", 9.75f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        }
        catch (Exception ex)
        {
            // Font fallback only fails in exotic environments; the system font always exists.
            Debug.WriteLine($"TrayService menu font fallback: {ex}");
            var systemFont = Drawing.SystemFonts.MenuFont;
            return systemFont is not null
                ? (Drawing.Font)systemFont.Clone()
                : new Drawing.Font(Drawing.FontFamily.GenericSansSerif, 9f);
        }
    }

    private WinForms.ToolStripMenuItem CreateMenuItem(Action onClick)
    {
        var item = new WinForms.ToolStripMenuItem
        {
            Padding = new WinForms.Padding(6, 4, 10, 4)
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    // ==================================================================== menu labels / state

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) => RebuildMenuTexts();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        UiDispatcher.Post(RebuildMenuTexts);
    }

    private void RebuildMenuTexts()
    {
        if (_disposed) return;

        var menu = _menu;
        var open = _openItem;
        var toggle = _toggleItem;
        var settings = _settingsItem;
        var exit = _exitItem;
        if (menu is null || open is null || toggle is null || settings is null || exit is null) return;

        try
        {
            var arabic = Loc.Instance.IsArabic;
            var rtl = arabic ? WinForms.RightToLeft.Yes : WinForms.RightToLeft.No;

            open.Text = Loc.T("Tray.Open");
            toggle.Text = FusionActive ? Loc.T("Fusion.Disengage") : Loc.T("Fusion.Engage");
            settings.Text = Loc.T("TitleBar.Settings");
            exit.Text = Loc.T("Tray.Exit");

            menu.RightToLeft = rtl;
            foreach (WinForms.ToolStripItem item in menu.Items) item.RightToLeft = rtl;

            // Re-read the palette so the menu follows the live accent / theme.
            menu.Renderer = new DarkMenuRenderer(DarkMenuColors.FromTheme());

            ApplyStateCore();
        }
        catch (Exception ex)
        {
            // Cosmetic refresh only — a failed label update must not break the tray.
            Debug.WriteLine($"TrayService.RebuildMenuTexts failed: {ex}");
        }
    }

    private bool FusionActive
    {
        get
        {
            lock (_gate) return _fusionActive;
        }
    }

    private void ApplyStateCore()
    {
        if (_disposed) return;

        var icon = _notifyIcon;
        var toggle = _toggleItem;
        if (icon is null) return;

        try
        {
            if (toggle is not null)
            {
                toggle.Text = FusionActive ? Loc.T("Fusion.Disengage") : Loc.T("Fusion.Engage");
            }

            icon.Text = BuildTooltip();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayService.ApplyStateCore failed: {ex}");
        }
    }

    private string BuildTooltip()
    {
        bool fusion;
        string? game;
        lock (_gate)
        {
            fusion = _fusionActive;
            game = _gameName;
        }

        var state = Loc.T(fusion ? "Fusion.Active" : "Fusion.Inactive");
        var gameText = string.IsNullOrWhiteSpace(game) ? Loc.T("Fusion.NoGame") : game.Trim();
        var tooltip = $"{Loc.T("App.Name")} — {state} — {gameText}";

        return Fit(tooltip, TooltipMaxLength);
    }

    // ==================================================================== tray interaction

    /// <summary>NotifyIcon exposes DoubleClick with the plain EventHandler signature.</summary>
    private void OnTrayDoubleClick(object? sender, EventArgs e) => InvokeAction(_openApp);

    private void OnBalloonTipClicked(object? sender, EventArgs e) => InvokeAction(_openApp);

    /// <summary>Marshals the shell callback to the UI thread and swallows its exceptions.</summary>
    private void InvokeAction(Action? action)
    {
        if (action is null || _disposed) return;

        UiDispatcher.Post(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrayService action failed: {ex}");
            }
        });
    }

    private void RequestExit()
    {
        // Exit must travel through the main window's Closing pipeline: that is where the shell
        // asks for confirmation and restores every journaled Fusion change. Calling
        // Application.Shutdown() directly would bypass it, so close the owner window instead.
        // InvokeAction already marshals this to the UI thread.
        var owner = _owner;
        if (owner is not null)
        {
            try
            {
                if (owner.Dispatcher.CheckAccess()) owner.Close();
                else owner.Dispatcher.Invoke(owner.Close);
            }
            catch (Exception ex)
            {
                // Close() throws while the close pipeline is already running; shutting down here would cut its restore short.
                Debug.WriteLine($"TrayService.RequestExit could not close the owner window: {ex}");
            }
            return;
        }

        // Fallback (no owner, or its close path failed): best-effort direct shutdown.
        try
        {
            System.Windows.Application.Current?.Shutdown();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"TrayService.RequestExit could not call Application.Shutdown: {ex}");
        }
    }

    private void OnApplicationExit(object? sender, ExitEventArgs e) => Dispose();

    // ==================================================================== icon

    /// <summary>
    /// Loads <c>Assets/app.ico</c> from the WPF resources; falls back to the executable's
    /// associated icon and finally to the Windows shield so the tray is never iconless.
    /// </summary>
    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri(AppIconPackUri, UriKind.Absolute));
            if (resource?.Stream is not null)
            {
                using var stream = resource.Stream;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                buffer.Position = 0;
                return new Drawing.Icon(buffer);
            }
        }
        catch (Exception ex)
        {
            // Resource missing/renamed, not a valid ICO, or no Application on the thread.
            Debug.WriteLine($"TrayService could not load {AppIconPackUri}: {ex}");
        }

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                var extracted = Drawing.Icon.ExtractAssociatedIcon(executable);
                if (extracted is not null) return extracted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayService could not extract the executable icon: {ex}");
        }

        return (Drawing.Icon)Drawing.SystemIcons.Shield.Clone();
    }

    // ==================================================================== disposal

    private void DisposeCore()
    {
        try
        {
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            var app = System.Windows.Application.Current;
            if (app is not null) app.Exit -= OnApplicationExit;
        }
        catch (Exception ex)
        {
            // System.Windows.Application.Current may already be torn down during exit.
            Debug.WriteLine($"TrayService event detach failed: {ex}");
        }

        try
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.DoubleClick -= OnTrayDoubleClick;
                _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
                _notifyIcon.ContextMenuStrip = null;
                _notifyIcon.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayService.NotifyIcon dispose failed: {ex}");
        }
        finally
        {
            _notifyIcon = null;
        }

        try
        {
            if (_menu is not null)
            {
                _menu.Opening -= OnMenuOpening;
                _menu.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayService.ContextMenuStrip dispose failed: {ex}");
        }
        finally
        {
            _menu = null;
            _openItem = null;
            _toggleItem = null;
            _settingsItem = null;
            _exitItem = null;
        }

        try
        {
            _icon?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayService.Icon dispose failed: {ex}");
        }
        finally
        {
            _icon = null;
        }

        try
        {
            _menuFont?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayService menu font dispose failed: {ex}");
        }
        finally
        {
            _menuFont = null;
        }

        _initialized = false;
    }

    // ==================================================================== helpers

    private static string Fit(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength) return trimmed;

        var cut = Math.Max(1, maxLength - 1);
        return trimmed[..cut].TrimEnd() + "\u2026";
    }

    // ==================================================================== dark menu rendering

    /// <summary>Reads the live WPF palette so the WinForms menu matches the app theme.</summary>
    private sealed class DarkMenuColors : WinForms.ProfessionalColorTable
    {
        private DarkMenuColors(
            Drawing.Color background,
            Drawing.Color surface,
            Drawing.Color hover,
            Drawing.Color border,
            Drawing.Color borderStrong,
            Drawing.Color text,
            Drawing.Color textMuted,
            Drawing.Color accent)
        {
            Background = background;
            Surface = surface;
            Hover = hover;
            Border = border;
            BorderStrongColor = borderStrong;
            Text = text;
            TextMuted = textMuted;
            Accent = accent;
        }

        private Drawing.Color Background { get; }
        private Drawing.Color Surface { get; }
        private Drawing.Color Hover { get; }
        private Drawing.Color Border { get; }
        private Drawing.Color BorderStrongColor { get; }
        private Drawing.Color Text { get; }
        private Drawing.Color TextMuted { get; }
        private Drawing.Color Accent { get; }

        public Drawing.Color TextColor => Text;
        public Drawing.Color MutedColor => TextMuted;

        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color MenuBorder => BorderStrongColor;
        public override Drawing.Color MenuItemBorder => Accent;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Surface;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientMiddle => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Surface;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Border;
        public override Drawing.Color ToolStripBorder => BorderStrongColor;
        public override Drawing.Color CheckBackground => Surface;
        public override Drawing.Color CheckSelectedBackground => Hover;

        public static DarkMenuColors FromTheme()
        {
            static Drawing.Color FromBrush(string key, Drawing.Color fallback)
            {
                try
                {
                    if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
                    {
                        var color = brush.Color;
                        return Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TrayService theme colour '{key}' unavailable: {ex}");
                }

                return fallback;
            }

            return new DarkMenuColors(
                FromBrush("Brush.SurfaceAlt", Drawing.Color.FromArgb(0x12, 0x1C, 0x2F)),
                FromBrush("Brush.Surface", Drawing.Color.FromArgb(0x0E, 0x16, 0x26)),
                FromBrush("Brush.SurfaceHover", Drawing.Color.FromArgb(0x17, 0x24, 0x3A)),
                FromBrush("Brush.Border", Drawing.Color.FromArgb(0x1C, 0x2A, 0x42)),
                FromBrush("Brush.BorderStrong", Drawing.Color.FromArgb(0x2A, 0x3D, 0x5D)),
                FromBrush("Brush.Text", Drawing.Color.FromArgb(0xE9, 0xF0, 0xFB)),
                FromBrush("Brush.TextMuted", Drawing.Color.FromArgb(0x93, 0xA5, 0xBF)),
                FromBrush("Brush.Accent", Drawing.Color.FromArgb(0x22, 0xD3, 0xEE)));
        }
    }

    private sealed class DarkMenuRenderer(DarkMenuColors colors)
        : WinForms.ToolStripProfessionalRenderer(colors)
    {
        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item?.Enabled == false ? colors.MutedColor : colors.TextColor;
            base.OnRenderItemText(e);
        }
    }
}
