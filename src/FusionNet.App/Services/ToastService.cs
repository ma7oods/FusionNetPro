using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.Core.Models;

namespace FusionNet.App.Services;

/// <summary>Applies one of the five accent palettes to the live resource dictionary.</summary>
public static class ThemeService
{
    private static readonly Dictionary<string, (Color Accent, Color Accent2)> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cyan"] = (Color.FromRgb(0x5A, 0xA9, 0xFF), Color.FromRgb(0x64, 0xB5, 0xF6)),
        ["violet"] = (Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x7C, 0x3A, 0xED)),
        ["emerald"] = (Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x10, 0xB9, 0x81)),
        ["amber"] = (Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xF5, 0x9E, 0x0B)),
        ["rose"] = (Color.FromRgb(0xFB, 0x71, 0x85), Color.FromRgb(0xE1, 0x1D, 0x48))
    };

    public static IReadOnlyList<string> Accents => FusionNet.Core.Models.AccentCatalog.All;

    public static string CurrentAccent { get; private set; } = FusionNet.Core.Models.AccentCatalog.DefaultId;

    public static event EventHandler<string>? AccentChanged;

    public static void Initialize(string? accentId) => Apply(accentId, raiseEvent: false);

    public static void Apply(string? accentId, bool raiseEvent = true)
    {
        var id = accentId is not null && Palettes.ContainsKey(accentId) ? accentId.ToLowerInvariant() : AccentCatalog.DefaultId;
        CurrentAccent = id;

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            if (raiseEvent) AccentChanged?.Invoke(null, id);
            return;
        }

        var (accent, accent2) = Palettes[id];
        var accentInk = Color.FromRgb(0x04, 0x12, 0x1A);

        resources["Color.Accent"] = accent;
        resources["Color.Accent2"] = accent2;
        resources["Color.AccentInk"] = accentInk;
        resources["Brush.Accent"] = Freeze(new SolidColorBrush(accent));
        resources["Brush.Accent2"] = Freeze(new SolidColorBrush(accent2));
        resources["Brush.AccentInk"] = Freeze(new SolidColorBrush(accentInk));
        resources["Brush.AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)));
        resources["Brush.AccentFaint"] = Freeze(new SolidColorBrush(Color.FromArgb(0x14, accent.R, accent.G, accent.B)));
        resources["Brush.AccentBorder"] = Freeze(new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)));

        var gradient = new LinearGradientBrush(accent, accent2, new Point(0, 0), new Point(1, 1));
        resources["Brush.AccentGradient"] = Freeze(gradient);
        var horizontal = new LinearGradientBrush(accent, accent2, new Point(0, 0), new Point(1, 0));
        resources["Brush.AccentGradientHorizontal"] = Freeze(horizontal);

        var spark = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x66, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0x00, 0x0C, 0x14, 0x22), 1)
            }
        };
        resources["Brush.SparkGradient"] = Freeze(spark);

        if (raiseEvent) AccentChanged?.Invoke(null, id);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class ToastItem : ViewModelBase
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required ToastKind Kind { get; init; }

    public string Glyph => Kind switch
    {
        ToastKind.Success => "\uE73E",
        ToastKind.Warning => "\uE7BA",
        ToastKind.Error => "\uE783",
        _ => "\uE946"
    };
}

/// <summary>Lightweight in-window notification centre; the shell renders <c>ToastHost</c>.</summary>
public sealed class ToastService
{
    private readonly object _gate = new();

    public static ToastService Instance { get; } = new();

    public ObservableCollection<ToastItem> Items { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Info, string? title = null, int durationMs = 4200)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var titleText = title ?? kind switch
        {
            ToastKind.Success => Loc.T("Common.Success"),
            ToastKind.Warning => Loc.T("Common.Warning"),
            ToastKind.Error => Loc.T("Common.Error"),
            _ => Loc.T("Common.Notice")
        };

        var item = new ToastItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = titleText,
            Message = message,
            Kind = kind
        };

        UiDispatcher.Post(() =>
        {
            lock (_gate)
            {
                while (Items.Count >= 4) Items.RemoveAt(0);
                Items.Add(item);
            }

            _ = RemoveLaterAsync(item, Math.Clamp(durationMs, 1200, 15000));
        });
    }

    public void ShowLocalized(string messageKey, ToastKind kind = ToastKind.Info, string? title = null) =>
        Show(Loc.T(messageKey), kind, title);

    public void ShowLocalizedFormat(string messageKey, ToastKind kind, params object?[] args) =>
        Show(Loc.Instance.FormatText(messageKey, args), kind);

    public void Dismiss(ToastItem item)
    {
        UiDispatcher.Post(() =>
        {
            lock (_gate) Items.Remove(item);
        });
    }

    private async Task RemoveLaterAsync(ToastItem item, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        UiDispatcher.Post(() =>
        {
            lock (_gate) Items.Remove(item);
        });
    }
}

