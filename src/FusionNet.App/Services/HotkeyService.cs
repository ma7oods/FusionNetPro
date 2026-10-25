using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FusionNet.App.Localization;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Services;

/// <summary>
/// One system-wide hotkey (default <c>Ctrl+Alt+F</c>) that toggles Fusion Mode.
/// Registration goes through <c>RegisterHotKey</c> on the main window's HWND and a
/// <see cref="HwndSource"/> hook; conflicts are reported as a localized message instead
/// of an exception, and the previous hotkey is always released before a new one is taken.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;

    // Win32 modifier flags accepted by RegisterHotKey.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>Swallows auto-repeat while the keys are held down.</summary>
    private const uint ModNoRepeat = 0x4000;

    // Application hotkey ids are valid in 0x0000-0xBFFF; stay well inside the range.
    private const int FirstHotkeyId = 0x4E50; // "NP"
    private const int LastHotkeyId = 0x4E50 + 0x3FF;

    public static HotkeyService Instance { get; } = new();

    private readonly HwndSourceHook _hook;

    private HwndSource? _source;
    private Window? _window;
    private Window? _pendingWindow;
    private HotkeyGesture? _pendingGesture;
    private Dispatcher? _dispatcher;
    private IntPtr _handle;
    private int _id;
    private int _nextId = FirstHotkeyId;
    private bool _hooked;
    private bool _disposed;

    private HotkeyService() => _hook = WndProc;

    /// <summary>Raised on the UI thread while the registered chord is pressed.</summary>
    public event EventHandler? Pressed;

    public bool IsRegistered => _id != 0;

    /// <summary>Last localized registration error, or <c>null</c> when the last attempt succeeded.</summary>
    public string? LastError { get; private set; }

    // ==================================================================== public API

    /// <summary>
    /// Registers <paramref name="gesture"/> for the lifetime of <paramref name="window"/>.
    /// When the window has no HWND yet the call is deferred until <c>SourceInitialized</c>
    /// and still returns <c>true</c>; a Win32 conflict returns <c>false</c> plus a localized
    /// message that the settings page can display.
    /// </summary>
    public bool Register(Window window, HotkeyGesture gesture, out string? error)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (gesture is null)
        {
            error = Loc.T("Hotkey.Unsupported");
            LastError = error;
            return false;
        }

        var dispatcher = window.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            error = Loc.T("Hotkey.NotReady");
            LastError = error;
            return false;
        }

        if (!dispatcher.CheckAccess())
        {
            var registered = false;
            string? innerError = null;

            try
            {
                dispatcher.Invoke(() =>
                {
                    registered = RegisterCore(window, gesture, out var coreError);
                    innerError = coreError;
                });
            }
            catch (Exception ex)
            {
                // Dispatcher already shutting down: the hotkey is irrelevant at that point.
                Debug.WriteLine($"HotkeyService.Register could not marshal to the UI thread: {ex}");
                innerError = Loc.T("Hotkey.NotReady");
                registered = false;
            }

            error = innerError;
            return registered;
        }

        return RegisterCore(window, gesture, out error);
    }

    /// <summary>Releases the current hotkey (safe to call repeatedly and from any thread).</summary>
    public void Unregister()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            try
            {
                dispatcher.Invoke(UnregisterCore);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HotkeyService.Unregister could not marshal to the UI thread: {ex}");
            }

            return;
        }

        UnregisterCore();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }

    // ==================================================================== registration

    private bool RegisterCore(Window window, HotkeyGesture gesture, out string? error)
    {
        if (_disposed)
        {
            error = Loc.T("Hotkey.NotReady");
            LastError = error;
            return false;
        }

        // A modifier-less key or an unmapped key would either be rejected by the shell or
        // hijack a plain key globally; refuse both with a friendly message.
        if (!gesture.HasModifier || gesture.ToVirtualKey() == 0)
        {
            UnregisterCore();
            error = Loc.T("Hotkey.Unsupported");
            LastError = error;
            return false;
        }

        // Never leave a stale registration behind when re-registering.
        UnregisterCore();

        _window = window;
        _dispatcher = window.Dispatcher;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            // The HWND does not exist yet: finish once WPF raises SourceInitialized.
            _pendingWindow = window;
            _pendingGesture = gesture;
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            error = null;
            LastError = null;
            return true;
        }

        return TryRegisterCore(handle, gesture, out error);
    }

    private bool TryRegisterCore(IntPtr handle, HotkeyGesture gesture, out string? error)
    {
        error = null;

        var window = _window;
        var source = HwndSource.FromHwnd(handle);
        if (source is null)
        {
            error = Loc.T("Hotkey.NotReady");
            LastError = error;
            return false;
        }

        var id = NextId();
        if (!RegisterHotKey(handle, id, ToWin32Modifiers(gesture), gesture.ToVirtualKey()))
        {
            // 1409 = ERROR_HOTKEY_ALREADY_REGISTERED, but any code is useful in the report.
            var code = Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
            error = $"{Loc.T("Hotkey.Failed")} ({code})";
            LastError = error;
            return false;
        }

        try
        {
            source.AddHook(_hook);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HotkeyService.AddHook failed: {ex}");
            UnregisterHotKey(handle, id);
            error = Loc.T("Hotkey.NotReady");
            LastError = error;
            return false;
        }

        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Closed += OnWindowClosed;
            window.SourceInitialized -= OnSourceInitialized;
        }

        _source = source;
        _hooked = true;
        _handle = handle;
        _id = id;
        _pendingWindow = null;
        _pendingGesture = null;
        LastError = null;
        return true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var window = _pendingWindow ?? sender as Window;
        var gesture = _pendingGesture;
        if (window is null || gesture is null) return;

        window.SourceInitialized -= OnSourceInitialized;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            _pendingWindow = null;
            _pendingGesture = null;
            LastError = Loc.T("Hotkey.NotReady");
            return;
        }

        TryRegisterCore(handle, gesture, out _);
    }

    private void UnregisterCore()
    {
        var window = _window;
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.SourceInitialized -= OnSourceInitialized;
        }

        _pendingWindow = null;
        _pendingGesture = null;
        _window = null;
        _dispatcher = null;

        var handle = _handle;
        var id = _id;
        _handle = IntPtr.Zero;
        _id = 0;

        if (id != 0 && handle != IntPtr.Zero)
        {
            try
            {
                UnregisterHotKey(handle, id);
            }
            catch (Exception ex)
            {
                // The HWND may already be gone (window closed); nothing left to release.
                Debug.WriteLine($"HotkeyService.UnregisterHotKey failed: {ex}");
            }
        }

        var source = _source;
        _source = null;
        if (_hooked && source is not null)
        {
            try
            {
                source.RemoveHook(_hook);
            }
            catch (Exception ex)
            {
                // The source is dead; its hooks died with it.
                Debug.WriteLine($"HotkeyService.RemoveHook failed: {ex}");
            }
        }

        _hooked = false;
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Unregister();

    private int NextId()
    {
        _nextId++;
        if (_nextId is < FirstHotkeyId or > LastHotkeyId) _nextId = FirstHotkeyId;
        return _nextId;
    }

    // ==================================================================== window proc

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || _id == 0 || wParam.ToInt32() != _id) return IntPtr.Zero;

        handled = true;
        try
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A buggy subscriber must not crash the WM_HOTKEY pump.
            Debug.WriteLine($"HotkeyService.Pressed subscriber failed: {ex}");
        }

        return IntPtr.Zero;
    }

    // ==================================================================== win32

    private static uint ToWin32Modifiers(HotkeyGesture gesture)
    {
        uint modifiers = 0;
        if (gesture.Ctrl) modifiers |= ModControl;
        if (gesture.Alt) modifiers |= ModAlt;
        if (gesture.Shift) modifiers |= ModShift;
        if (gesture.Win) modifiers |= ModWin;
        return modifiers | ModNoRepeat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>Strings owned by the UI-infrastructure layer (registered before app startup).</summary>
internal static class HotkeyStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        Loc.Add(
            "Hotkey.Failed",
            "This shortcut is already used by another app. Choose a different one.",
            "هذا الاختصار مستخدَم من تطبيق آخر. اختر اختصاراً مختلفاً.");
        Loc.Add(
            "Hotkey.Unsupported",
            "This key cannot be used for a global shortcut.",
            "هذا المفتاح لا يصلح لاختصار عام.");
        Loc.Add(
            "Hotkey.NotReady",
            "The window is not ready yet. Try again in a moment.",
            "النافذة غير جاهزة بعد. أعد المحاولة بعد لحظات.");
    }
}
