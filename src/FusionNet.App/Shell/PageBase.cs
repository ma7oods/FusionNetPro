using System.Windows.Controls;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.App.Shell;

/// <summary>Marks a page UserControl so the shell can discover it, order it and label it.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PageInfoAttribute(
    string key,
    string titleKey,
    string glyph,
    int order = 100,
    string groupKey = "Nav.Group.System") : Attribute
{
    public string Key { get; } = key;
    public string TitleKey { get; } = titleKey;
    public string Glyph { get; } = glyph;
    public int Order { get; } = order;
    public string GroupKey { get; } = groupKey;
}

/// <summary>
/// Base class for every page. The shell creates one instance lazily, calls
/// <see cref="OnActivatedAsync"/> when shown and <see cref="OnDeactivated"/> when hidden,
/// so pages can subscribe/unsubscribe to live service events.
/// </summary>
public abstract class PageBase : UserControl
{
    public IAppServices Services => AppServices.Current;

    public virtual Task OnActivatedAsync() => Task.CompletedTask;

    public virtual void OnDeactivated()
    {
    }

    public virtual void OnSettingsChanged(FusionSettings settings)
    {
    }

    /// <summary>Called when the user switches Arabic/English so pages can refresh computed labels.</summary>
    public virtual void OnLanguageChanged()
    {
    }
}
