using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FusionNet.App.Localization;
using FusionNet.App.Services;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;
using FusionNet.Windows;
using FusionNet.Windows.System;
using FusionNet.Windows.Monitoring;
using System.Text;
using System.Text.Json;

namespace FusionNet.App;

public partial class App : Application
{
    private const string MutexName = @"Local\FusionNetPro.SingleInstance";
    private Mutex? _instanceMutex;
    private IAppServices? _services;
    private bool _exiting;
    private bool _workerOnly;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var restoreArgument = e.Args.SingleOrDefault(arg => arg.StartsWith("--gameboost-restore=", StringComparison.Ordinal));
        if (restoreArgument is not null)
        {
            _workerOnly = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunGameBoostRestoreAsync(restoreArgument["--gameboost-restore=".Length..]);
            return;
        }

        var trafficArgument = e.Args.SingleOrDefault(arg => arg.StartsWith("--traffic-worker=", StringComparison.Ordinal));
        if (trafficArgument is not null)
        {
            _workerOnly = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunTrafficWorkerAsync(trafficArgument["--traffic-worker=".Length..]);
            return;
        }

        var workerArgument = e.Args.SingleOrDefault(arg => arg.StartsWith("--gameboost-worker=", StringComparison.Ordinal));
        if (workerArgument is not null)
        {
            _workerOnly = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunGameBoostWorkerAsync(workerArgument["--gameboost-worker=".Length..]);
            return;
        }

        AppPaths.EnsureDirectories();
        RegisterCrashHandlers();

        var wantsElevated = e.Args.Any(arg => string.Equals(arg, ElevationService.ElevatedArgument, StringComparison.OrdinalIgnoreCase));
        if (!TryClaimSingleInstance(wantsElevated))
        {
            Shutdown(0);
            return;
        }

        IAppServices services;
        try
        {
            services = WindowsServices.Create(new WpfFileSaveService());
        }
        catch (Exception ex)
        {
            ReportFatal("FusionNet Pro could not start its services.", ex);
            Shutdown(1);
            return;
        }

        _services = services;
        AppServices.Initialize(services);

        // Arabic is the product default. FusionSettings.Normalize currently pins Language to "en"
        // (frozen Core contract), so the UI keeps its own persisted preference for AR/EN.
        Loc.Instance.SetLanguage(UiLanguagePreference.Load(), persist: false);
        Loc.Instance.Persist += OnLanguagePersist;
        ThemeService.Initialize(services.Settings.Accent);

        // HasCompletedSetup stays false until the welcome guide is finished (shell marks it).

        var window = new MainWindow();
        MainWindow = window;

        // QA/automation helper: fixed window size for layout shots, e.g. --size=1366x768
        ApplyRequestedWindowSize(window, e.Args);

        window.Show();

        // Automation / QA helper: start on a specific page, e.g. --page=dns
        var pageArgument = e.Args
            .FirstOrDefault(arg => arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        if (pageArgument is not null)
        {
            var pageKey = pageArgument["--page=".Length..].Trim();
            if (pageKey.Length > 0) window.NavigateToPage(pageKey);
        }

        // Offline render mode for documentation/QA: --screenshot=<page> --out=<path.png>
        var screenshotArgument = e.Args
            .FirstOrDefault(arg => arg.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase));
        if (screenshotArgument is not null)
        {
            var pageKey = screenshotArgument["--screenshot=".Length..].Trim();
            var outArgument = e.Args.FirstOrDefault(arg => arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase));
            var outPath = outArgument?["--out=".Length..].Trim();
            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.Combine(AppPaths.ReportDirectory, $"screenshot-{pageKey}.png");

            if (pageKey.Length > 0) window.NavigateToPage(pageKey);
            _ = CaptureAndExitAsync(window, outPath);
        }

        // Offline render mode exits without the restore path, so it must never arm automation.
        _ = window.InitializeRuntimeAsync(armAutomation: screenshotArgument is null);

        if (services is WindowsServices windowsServices)
            windowsServices.ExitRequested += OnExitRequested;
    }

    private async Task RunGameBoostWorkerAsync(string encoded)
    {
        var code = 1;
        try
        {
            if (!ElevationService.CheckIsElevated() || encoded.Length > 24000) throw new InvalidOperationException("Invalid worker invocation.");
            var request = JsonSerializer.Deserialize<GameBoostWorkerRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))
                ?? throw new InvalidOperationException("Invalid worker request.");
            code = await Task.Run(() => new GameBoostWorker(request).RunAsync());
        }
        catch { /* A rejected helper must never start normal application services or automation. */ }
        Shutdown(code);
    }

    private async Task RunTrafficWorkerAsync(string encoded)
    {
        var code = 1;
        try
        {
            if (!ElevationService.CheckIsElevated() || encoded.Length > 8000) throw new InvalidOperationException("Invalid traffic worker invocation.");
            var request = JsonSerializer.Deserialize<TrafficSampleRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))
                ?? throw new InvalidOperationException("Invalid traffic request.");
            code = await Task.Run(() => WindowsTrafficSampler.RunWorkerAsync(request));
        }
        catch { /* No fallback measurements or normal app startup on worker failure. */ }
        Shutdown(code);
    }
    private async Task RunGameBoostRestoreAsync(string id)
    {
        var code = 1;
        if (Guid.TryParseExact(id, "N", out var sessionId))
            code = await Task.Run(() => GameBoostWorker.RestoreSavedAsync(sessionId));
        Shutdown(code);
    }

    private static void ApplyRequestedWindowSize(Window window, string[] args)
    {
        var sizeArgument = args.FirstOrDefault(arg => arg.StartsWith("--size=", StringComparison.OrdinalIgnoreCase));
        if (sizeArgument is null) return;

        var parts = sizeArgument["--size=".Length..].Split('x', 'X');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[0].Trim(), out var width) || !int.TryParse(parts[1].Trim(), out var height)) return;

        window.Width = Math.Clamp(width, window.MinWidth, 5120);
        window.Height = Math.Clamp(height, window.MinHeight, 2880);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    // Windows shutdown/logoff closes windows ignoring Cancel, so MainWindow.OnClosing cannot finish a restore.
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (_workerOnly || _services is null || e.Cancel) return;
        try
        {
            _services.Fusion.StopAutomation();
            if (_services.Fusion.State is FusionModeState.Active or
                FusionModeState.Arming or FusionModeState.Restoring)
                _services.Fusion.DisengageAsync().Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Session-end restore failed: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_workerOnly) { base.OnExit(e); return; }
        if (_exiting)
        {
            base.OnExit(e);
            return;
        }

        _exiting = true;

        try
        {
            Loc.Instance.Persist -= OnLanguagePersist;

            if (_services is not null)
            {
                try
                {
                    _services.Settings.LastRunUtc = DateTimeOffset.UtcNow;
                    _services.SaveSettings();
                }
                catch
                {
                    // best effort persistence on shutdown
                }

                (_services as IDisposable)?.Dispose();
            }

            TrayService.Instance.Dispose();
            HotkeyService.Instance.Dispose();
        }
        catch
        {
            // never block shutdown
        }
        finally
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private bool TryClaimSingleInstance(bool waitForPreviousInstance)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (isFirst) return true;

        if (!waitForPreviousInstance)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }

        // An elevated relaunch starts while the old instance is still exiting. Its exit can wait up to
        // 15 s for a Game Boost restore (plus a possible UAC prompt), so allow well beyond that.
        _instanceMutex.Dispose();
        _instanceMutex = null;
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out isFirst);
            if (isFirst) return true;
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        return false;
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        if (MainWindow is { } window) window.Close();
        else Shutdown(0);
    }

    /// <summary>
    /// Renders the live window to a PNG without screen capture, then exits (documentation/QA).
    /// This path deliberately bypasses the interactive close-confirmation pipeline: automation
    /// must never wait on a modal dialog.
    /// </summary>
    private async Task CaptureAndExitAsync(Window window, string outPath)
    {
        try
        {
            // Let the page activate, bind and receive its first live monitor samples.
            await Task.Delay(3200).ConfigureAwait(true);
            await WaitForLayoutAsync(window, TimeSpan.FromSeconds(8)).ConfigureAwait(true);

            var saved = false;
            try
            {
                window.Dispatcher.Invoke(() => RenderWindowToPng(window, outPath));
                saved = true;
            }
            catch (Exception first)
            {
                System.Diagnostics.Debug.WriteLine($"[Screenshot] first attempt failed: {first}");
                await Task.Delay(500).ConfigureAwait(true);
                try
                {
                    window.Dispatcher.Invoke(() => RenderWindowToPng(window, outPath));
                    saved = true;
                }
                catch (Exception ex)
                {
                    ReportCrash("Screenshot", ex);
                }
            }

            if (saved) System.Diagnostics.Debug.WriteLine($"[Screenshot] saved {outPath}");
        }
        catch (Exception ex)
        {
            ReportCrash("Screenshot", ex);
        }
        finally
        {
            // Never run the window's Closing handlers in automation mode: the app may ask the
            // user to confirm restoring an active session, which would hang a headless run.
            Shutdown(0);
        }
    }

    private static async Task WaitForLayoutAsync(Window window, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var ready = false;
            await window.Dispatcher.InvokeAsync(() =>
            {
                window.UpdateLayout();
                if (window.Content is FrameworkElement root)
                    ready = root.ActualWidth > 1 && root.ActualHeight > 1;
            });
            if (ready) return;
            await Task.Delay(120).ConfigureAwait(true);
        }
    }

    private static void RenderWindowToPng(Window window, string outPath)
    {
        window.UpdateLayout();
        var root = window.Content as FrameworkElement
                   ?? throw new InvalidOperationException("The window has no content to render.");

        var width = (int)Math.Ceiling(root.ActualWidth);
        var height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("The window has no layout yet.");

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        // Resolve the output path once: the file must land in the directory that was just
        // created, otherwise a relative --out path can miss its own parent folder.
        var fullPath = Path.GetFullPath(outPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var stream = File.Create(fullPath);
        encoder.Save(stream);
    }

    private void OnLanguagePersist(object? sender, string language)
    {
        if (_services is null) return;

        UiLanguagePreference.Save(language);

        _services.Settings.Language = language;
        try
        {
            _services.SaveSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Loc] Could not persist language: {ex.Message}");
        }
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ReportCrash("UI thread", args.Exception);
            ToastService.Instance.Show(
                Loc.T("Common.Error") + ": " + args.Exception.Message,
                ToastKind.Error,
                Loc.T("Common.Error"));
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportCrash("AppDomain", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            ReportCrash("Background task", args.Exception);
        };
    }

    private static void ReportCrash(string source, Exception? exception)
    {
        if (exception is null) return;
        System.Diagnostics.Debug.WriteLine($"[Crash:{source}] {exception}");

        try
        {
            AppPaths.EnsureDirectories();
            var path = Path.Combine(AppPaths.CrashDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            File.AppendAllText(path, $"Source: {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // crash logging must never throw
        }
    }

    private static void ReportFatal(string message, Exception exception)
    {
        ReportCrash("Startup", exception);
        MessageBox.Show(
            $"{message}{Environment.NewLine}{Environment.NewLine}{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{Environment.NewLine}{Loc.T("Label.Details")}: %LOCALAPPDATA%\\{AppPaths.Slug}\\crashes",
            "FusionNet Pro",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
