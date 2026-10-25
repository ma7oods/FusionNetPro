using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace FusionNet.App.Localization;

/// <summary>
/// Bilingual string engine. Every visible string is looked up through <see cref="Instance"/>,
/// which is an INotifyPropertyChanged indexer, so switching language updates the whole UI live.
/// Pages register their own keys from a [ModuleInitializer] file (see docs/UI_GUIDE.md).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _en = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ar = new(StringComparer.Ordinal);
    private string _language = "en";

    public static Loc Instance { get; } = new();

    private Loc()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string Language => _language;
    public bool IsArabic => _language == "ar";
    public FlowDirection FlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>XAML indexer: <c>{Binding Source={x:Static loc:Loc.Instance}, Path=[Nav.Dashboard]}</c>.</summary>
    public string this[string key] => Get(key);

    public string Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (_language == "ar" && _ar.TryGetValue(key, out var ar)) return ar;
        if (_en.TryGetValue(key, out var en)) return en;
        return key;
    }

    public string FormatText(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>Static form: <c>Loc.Format("Key", arg)</c>.</summary>
    public static string Format(string key, params object?[] args) => Instance.FormatText(key, args);

    public void SetLanguage(string? language, bool persist = true)
    {
        var normalized = string.Equals(language?.Trim(), "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        if (normalized == _language)
        {
            if (persist) Persist?.Invoke(this, normalized);
            return;
        }

        _language = normalized;
        if (persist) Persist?.Invoke(this, normalized);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsArabic)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public void Toggle() => SetLanguage(IsArabic ? "en" : "ar");

    /// <summary>Raised whenever the user changes language so the shell can persist it.</summary>
    public event EventHandler<string>? Persist;

    public static string T(string key) => Instance.Get(key);

    public static string Format2(string key, params object?[] args) => Instance.FormatText(key, args);

    public static void Add(string key, string english, string arabic)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        Instance._en[key] = english;
        Instance._ar[key] = arabic;
    }

    internal static void Reload() =>
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>Markup extension: <c>Text="{loc:T Common.Apply}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
